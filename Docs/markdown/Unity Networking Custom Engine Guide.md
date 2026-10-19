# **Architecture and Implementation of Modern Game Engine Networking: An Analysis of Unity's Ecosystem and Custom Engine Design**

The development of a robust networking architecture for real-time multiplayer simulations requires a profound understanding of low-level data transmission, state synchronization, bandwidth optimization, and latency mitigation. Standard networking protocols like the Transmission Control Protocol (TCP) guarantee reliable, ordered delivery but introduce unbounded latency through head-of-line blocking, rendering them unsuitable for fast-paced interactive environments. Consequently, modern game engines rely on the User Datagram Protocol (UDP), implementing custom reliability, sequencing, and congestion control algorithms at the application layer.  
This report provides an exhaustive analysis of how the Unity engine structures its networking ecosystem—spanning the low-level Unity Transport Package (UTP), the high-level Netcode for GameObjects (NGO), and the data-oriented Netcode for Entities (NFE). Building upon these architectural paradigms, alongside industry-standard techniques utilized in engines like Unreal and Quake, the subsequent sections deliver a comprehensive blueprint for engineering a state-of-the-art networking stack within a custom game engine.

## **The Foundation: Unity Transport Package Architecture**

At the lowest level of Unity's network architecture is the Unity Transport Package (UTP), a transport-agnostic framework designed to handle the transmission of raw byte streams1. By default, UTP operates over UDP, but its architecture is fundamentally modular, supporting custom transport implementations such as Apple Multipeer Connectivity, WebRTC, and platform-specific matchmaking services3.

### **NetworkDriver and Socket Abstraction**

The core of UTP is the NetworkDriver, which abstracts the underlying socket operations. The driver is responsible for binding to endpoints, managing connections, and exposing a unified API for sending and receiving packets2. Because UTP is heavily integrated with Unity's Burst compiler and C\# Job System, its unmanaged memory interface (INetworkInterface) is designed to maximize throughput by operating entirely outside the managed garbage collection environment5.  
Starting with Unity 6.6, UTP expanded its capabilities to allow third-party transports that utilize managed types (NetworkTransport) to be wrapped into the transport pipeline6. This accommodation bridges the gap for software development kits (SDKs) that may not be Burst-compatible. However, for architectures scaling to hundreds of concurrent players, the unmanaged INetworkInterface paired with Burst-compiled jobs remains the required standard to prevent main-thread bottlenecks6. Furthermore, the NetworkDriver automatically disposes of network interfaces before domain reloads to prevent memory leaks during rapid iteration in the editor7.

### **Pipeline Stage Architecture**

A raw UDP socket provides no guarantees regarding packet delivery, order, or duplication. UTP resolves this through a sophisticated pipeline architecture, wherein data passes through discrete processing stages before reaching the wire3. These pipelines are initialized during driver creation, allowing developers to inject custom pipeline stages into a NetworkPipelineStageCollection3.

| Pipeline Stage | Functional Description | Protocol Mechanics |
| :---- | :---- | :---- |
| **Unreliable Sequenced** | Drops packets that arrive out of order, prioritizing the most recent state. | Injects a sequence ID. If the received ID is lower than the highest known ID, the packet is discarded, and bandwidth statistics are calculated2. |
| **Reliable Sequenced** | Guarantees delivery and order, essential for events like Remote Procedure Calls (RPCs). | Utilizes a sliding window protocol (default size 32\) and an Acknowledgement (ACK) mask. Requires retransmission of unacknowledged IDs3. |
| **Fragmentation** | Splits payloads larger than the Maximum Transmission Unit (MTU). | Appends chunk metadata to reassemble oversized payloads at the destination3. |
| **Simulator Pipeline** | Injects artificial network latency and packet loss for localized testing. | Buffers packets dynamically based on configured Round Trip Time (RTT) parameters and stochastic drop percentages3. |

The reliable sequenced pipeline represents the most complex mathematical implementation within UTP. It utilizes a 32-bit ACK mask mechanism. Each reliable packet header contains a SequenceId (the identifier of the current packet), an AckedSequenceId (the highest sequence the sender has received from the remote endpoint), and an AckMask3. The bitmask represents the reception status of the 32 packets preceding the AckedSequenceId. If the sender does not observe an ACK for a specific sequence within its sliding window, it assumes the packet was dropped and triggers a retransmission3.  
The fragmentation stage ensures that payloads do not exceed the MTU. Unity Transport specifies an MTU constant, but integration with external services—such as Epic Online Services, which caps at 1170 bytes—often requires manual MTU parameterization to prevent routers from silently dropping oversized datagrams2.

## **Object-Oriented Networking: Netcode for GameObjects (NGO)**

Netcode for GameObjects (NGO) functions as a high-level software development kit built on top of UTP, designed specifically for Unity's traditional MonoBehaviour and GameObject ecosystem6. NGO operates primarily on a Host-Client or Dedicated Server topology, where a single authoritative instance governs the simulation state3.

### **Architectural Seams and Connection Management**

NGO abstracts the complexity of socket management into a singleton entity called the NetworkManager11. The boundary between NGO and the underlying transport is deliberately decoupled; NGO essentially hands packet delivery to whatever transport interface is injected underneath it10. While this transport-agnostic design allows the framework to interoperate seamlessly with external peer-to-peer (P2P) solutions or relay servers, it simultaneously obscures critical low-level parameters—such as tick rates, payload ceilings, and reliability modes—from the high-level API3.  
Third-party extensions, such as the Extreal NGO wrapper, explicitly demonstrate this decoupling by injecting alternative transport layers, such as WebRTC, directly into the NetworkManager via connection setters (e.g., WebRtcTransportConnectionSetter)4. Similarly, the Multipeer Connectivity transport adapts Apple's P2P framework into a Host-Client topology to satisfy NGO's rigid authoritative architecture requirements3. Additionally, Unity Relay acts as a STUN/TURN server intermediary, facilitating connections across complex NAT typologies while providing consistent remote address resolution12.

### **State Synchronization Mechanisms**

NGO synchronizes simulation state across the network through two primary mechanisms: NetworkVariables and Remote Procedure Calls (RPCs)11.  
NetworkVariables act as generic wrappers around primitive or user-defined data types that automatically replicate their state from the server to clients14. Upon the instantiation of a NetworkObject, the framework serializes the initial state of all associated NetworkVariables and transmits them across the wire. Subsequent changes are handled via delta messaging14. To prevent malicious clients from exploiting memory bounds, the NetworkManager enforces a NetworkVariable Length Safety configuration, which adds computational overhead but prevents buffer overflows during deserialization15.  
RPCs, conversely, are utilized for transient, ephemeral events rather than persistent state. The framework allows developers to flag specific functions to execute either on the server or on targeted clients, with built-in parameter serialization1.

### **Custom Serialization and Memory Management**

For complex, user-defined data structures, NGO bypasses standard C\# reflection-based serialization, instead utilizing the FastBufferWriter and FastBufferReader APIs17. These utility structures are designed for maximum throughput, writing raw bytes directly to memory using temporary allocation strategies (Allocator.Temp)19.  
A recurring architectural challenge with these buffers involves precise length calculation. The FastBufferWriter must accurately pre-calculate the required byte size before allocation20. Failure to align the exact write size with the read size results in "reading past the end of the buffer" exceptions during the deserialization phase21. This strict manual memory management is necessary because standard managed arrays introduce unacceptable garbage collection latency spikes during high-frequency network ticks.

## **Data-Oriented Networking: Netcode for Entities (NFE)**

Where NGO prioritizes accessibility for object-oriented workflows, Netcode for Entities (NFE) is engineered for massive scale, operating within Unity's Entity Component System (ECS). In this paradigm, the monolithic GameObject is replaced by an "Entity," and network replication is handled through highly optimized structures known as "Ghosts"22.

### **Ghost Snapshots and Relevancy**

A Ghost is a networked entity simulated authoritatively by the server. During every simulation frame (tick), the server captures a snapshot of the current state of all active Ghosts and broadcasts this snapshot to connected clients22. Because a dense simulation might contain tens of thousands of dynamic entities, attempting to replicate the entire state space simultaneously would instantly saturate the network's bandwidth15.  
To resolve this bottleneck, NFE evaluates an Importance metric for each Ghost15. The importance dictates which entities are prioritized for transmission when the outgoing packet queue is full. To maximize CPU throughput, the server processes this importance evaluation per memory chunk rather than per entity15. Once a UDP packet reaches its MTU limit, remaining entities are omitted from the snapshot and queued for subsequent frames. As an entity's data ages, the framework scales its importance upward, ensuring eventual consistency15. Furthermore, importance can be scaled dynamically based on spatial distance, prioritizing the replication of entities closest to the observing client15.

### **Optimization and Transmission Modes**

NFE provides granular control over how Ghost component data is packaged and transmitted over the wire22. The framework defines two distinct optimization modes for the overall Ghost prefab:

| Optimization Mode | Application | Bandwidth Characteristics |
| :---- | :---- | :---- |
| **Dynamic Optimization** | Entities in constant motion (e.g., projectiles, players). | Optimized to produce the smallest possible snapshot delta continually, regardless of whether the entity is moving or stationary15. |
| **Static Optimization** | Environment objects with infrequent state changes (e.g., doors, dropped items). | Does not aggressively optimize snapshot size during movement, but completely halts network transmission when the entity is at rest, virtually eliminating baseline bandwidth consumption15. |

At the component level, developers utilize the \[GhostComponentAttribute\] to instruct the runtime on replication behavior23. Components can be configured to strip themselves from specific versions of the ghost prefab (e.g., Interpolated Client, Predicted Client, or Server versions) to save memory footprint23. The attribute also controls whether data replicates to the owner of the entity, to non-owners, or to all clients.

### **Smoothing and Extrapolation Mechanics**

To handle the inherent jitter of network delivery, NFE implements robust smoothing mechanics on ghost fields. The SmoothingAction property dictates how a client visually processes arriving data when it is not actively predicting the ghost15.

| Smoothing Action | Visual Behavior | Primary Use Case |
| :---- | :---- | :---- |
| **Clamp** | Immediately jumps the client value to the latest snapshot value without smoothing. | Boolean states, integer counters, or teleportation events. |
| **Interpolate** | Linearly interpolates the field between the last two known snapshot values over time. | Standard movement for remote players and physics objects15. |
| **InterpolateAndExtrapolate** | Interpolates between known values, and if subsequent data fails to arrive in time, mathematically projects the value forward based on prior velocity. | High-speed entities where minor packet loss causes unacceptable visual stuttering15. |

Extrapolation is strictly clamped by the MaxExtrapolationTimeSimTicks parameter. Unbounded extrapolation results in severe desynchronization, causing entities to phase through solid geometry if the network connection drops entirely15. Additionally, developers can configure a MaxSmoothingDistance to automatically disable interpolation and snap the entity if the delta between two snapshots exceeds a physical threshold, a critical feature for handling intentional teleportation logic15.

## **Advanced Data Serialization and Bandwidth Optimization**

To implement a high-performance networking stack in a custom engine, it is necessary to move beyond standard programming paradigms. Transmitting raw 32-bit floats for position vectors and 64-bit integers for logic flags will rapidly saturate a client's network connection. Efficient game networking relies on dropping byte alignment in favor of continuous bit streams, quantizing floating-point values, and employing aggressive delta compression.

### **Network Serialization and Bit-Packing**

Standard programming languages (such as C++, C\#, or Rust) align struct variables to 4-byte or 8-byte boundaries to optimize CPU cache fetching. In networking, these padding bytes represent wasted bandwidth25. A proprietary network stack must serialize data strictly at the bit level.  
A custom BitWriter module appends variables consecutively into a byte buffer without padding. For example, a boolean value occupies exactly 1 bit, rather than a full 8-bit byte. An enumeration representing four possible weapon states occupies exactly 2 bits. When scaling to 64 concurrent players transmitting 60 times per second, this granular packing reduces payload sizes by massive margins.

### **Floating-Point Quantization**

Most spatial transformations in a 3D engine do not require IEEE 754 32-bit floating-point precision, which allocates 1 sign bit, 8 exponent bits, and 23 mantissa bits. If a game world is strictly bound to a specific dimension, positional floats can be quantized into integers.  
The mathematical process of quantizing a float within a known minimum and maximum bound into a specified number of bits is defined as follows:  
![][image1]  
Upon receiving the payload, the client decodes the bit-packed integer back into a floating-point value:  
![][image2]  
For instance, mapping a coordinate system ranging from \-1000 meters to 1000 meters into a 16-bit integer yields a spatial resolution of approximately 1.5 centimeters. This transformation cuts the bandwidth consumption of a vector in half, dropping from 96 bits to 48 bits, with negligible impact on visual fidelity.

### **Smallest-Three Quaternion Compression**

Sending a rotation as a standard quaternion requires four 32-bit floats (![][image3]), totaling 128 bits. However, because a valid spatial rotation quaternion is inherently normalized (![][image4]), it is mathematically impossible for all four components to be unknown if three are provided.  
The most efficient transmission method identifies the component with the largest absolute value. Since the sum of squares equals 1, the largest component must be at least 0.5, meaning its index can be stored in 2 bits (indicating 0, 1, 2, or 3). If this largest component is negative, the entire quaternion is negated (which preserves the identical rotation) to ensure the reconstructed largest component is always positive. The remaining three components are then quantized into 9 bits each. The total payload size drops from 128 bits to 29 bits (2 bits for the index \+ 27 bits for the quantized components)—a massive 77% reduction in bandwidth requirements.

### **Delta Compression and Change Masks**

Sending the full state of every object every frame is inherently inefficient. Delta compression solves this by transmitting only the modifications that have occurred since a mutually acknowledged baseline state22.  
The Quake 3 engine pioneered this approach, utilizing historical snapshots26. The server stores a rolling buffer of previous world states (snapshots) for each connected client, represented by snapshot\_t and entityState\_t structures. When a client sends a command packet, it includes the sequence ID of the last snapshot it successfully received. The server retrieves this specific snapshot from its history, compares it to the current authoritative world state, and transmits only the delta (the differences)25.  
To structure this within a serialized byte stream, engines utilize a Change Mask, commonly referred to as a Bitmask. A sequence of bits at the start of a network packet indicates which fields have changed. For example, if an entity contains 8 synchronizable properties, an 8-bit mask is transmitted. If the entity's position changed but its rotation did not, only the bit corresponding to position is flagged as 1\. The server then packs the quantized position data into the payload, and omits the rotation entirely15. Engines like Unreal expose deep configuration over these baselines, allowing developers to set the minimum number of frames between the creation of new delta compression baselines via console variables (e.g., a.Compression...) to tune memory optimization27.

## **Latency Mitigation and Timeline Synchronization**

Because network packets require physical time to travel across geographical distances, the client inherently views a past state of the simulation, and the server's responses are perpetually delayed28. Left untreated, this results in an unresponsive, sluggish experience. Mitigating latency involves managing independent, concurrent timelines on the client and the server.

### **Entity Interpolation and Buffering**

When a client receives snapshots from the server, applying the positional data directly to the renderer results in extreme visual stuttering, as packet delivery rates fluctuate due to network jitter29. To resolve this, engines implement an interpolation buffer.  
Instead of rendering the most recently received snapshot, the client intentionally delays rendering by a set duration (e.g., 50 milliseconds). This buffering window allows the client to smoothly interpolate the transform of an entity between two known snapshots, treating them as waypoints in the past22.  
In this system, multiple timelines exist concurrently22:

| Timeline Designation | Function and Scope |
| :---- | :---- |
| **Server Tick** | The authoritative present time of the simulation on the server22. |
| **Predicted Tick** | The client's present timeline, running slightly ahead of the server to process local inputs immediately22. |
| **Interpolation Tick** | The target tick in the past that the client is currently animating remote entities toward22. |

The progress between frames is tracked by the InterpolationTickFraction, a floating-point value (0.0 to 1.0) representing the exact progress between the previous snapshot and the target interpolation tick13. When this fraction hits 1.0, the client has reached the target tick and transitions to the next segment of the waypoint path22.

### **Client-Side Prediction and Server Reconciliation**

While interpolation is highly effective for remote entities (other players and physics objects), it is utterly unacceptable for the local player's character. If a player issues a movement command, they expect immediate feedback on their screen. Waiting for the command to travel to the server and for the resulting snapshot to return creates unplayable input lag.  
The solution is Client-Side Prediction. The client immediately executes the user's input and advances the local physics simulation, effectively running ahead of the server30. The client timestamps this input with a predicted sequence ID and transmits it to the server31.  
However, the server remains strictly authoritative. When the server eventually processes the input and broadcasts a snapshot back to the client, the client must verify its predicted state against the server's absolute state28.  
If the client's predicted state diverges from the server's snapshot—due to packet loss, a collision on the server that the client was unaware of, or floating-point non-determinism—a Misprediction occurs. To resolve this discrepancy, the engine executes a Server Reconciliation loop (often referred to as a Rollback)28.

#### **The Rollback and Resimulation Loop**

The rollback mechanism operates via a precise, multi-step algorithmic sequence executed within a single CPU frame:

> 1. **State Rollback:** The client halts its current simulation, overwriting its local historical state with the authoritative snapshot provided by the server for that specific tick23.  
> 2. **Input Purging:** The client discards all historical inputs in its local buffer that the server has definitively acknowledged in the new snapshot.  
> 3. **Resimulation (Fast-Forward Replay):** Starting from the freshly applied authoritative state, the client iteratively applies all remaining, unacknowledged inputs in its buffer. The engine rapidly re-simulates the physics and game logic for each input, fast-forwarding the entity back to the present predictive tick28.

Because this entire process occurs instantly between render frames, the mathematical correction is immediate30. However, large positional corrections can cause jarring visual teleports. To soften this impact, modern engines implement Prediction Smoothing, referred to in some architectures as "Incremental Bending"23. Rather than applying the correction instantly to the visual mesh, the underlying collision capsule is snapped to the correct location, while the rendering mesh's position, velocity, and angular velocity are smoothly blended toward the authoritative state over subsequent frames24.

#### **Determinism and Partial Tick Management**

A critical complexity in client-side prediction is the handling of simulation time versus rendering time. The client typically renders at a variable framerate, while physics engines require a fixed delta time to remain deterministic. If the prediction loop is misaligned with the fixed update schedule, the engine must simulate "partial ticks"30.  
A rollback occurs under two specific conditions: when a new snapshot arrives, or when the client must continue simulating a partial tick33. When a full simulation tick changes, the client samples the input and batches it. To calculate rollbacks during variable step frames, the client must restore the simulation state from the last full simulated tick backup, then re-run the variable step portion of the delta time23. A lack of strict determinism across these partial ticks guarantees endless prediction failures, destroying network bandwidth as the client constantly requests and executes corrections.

## **Architectural Blueprint for Custom Engine Implementation**

Transitioning from abstract networking theories to a concrete C++ (or equivalent systems language) custom engine implementation requires rigorous architectural layering. Attempting to intermingle socket transmission logic with gameplay state variables inevitably results in unmaintainable code and race conditions. The architecture must be divided into four distinct, hierarchical layers.

### **Layer 1: Socket Interface and Memory Management**

The lowest level is the SocketManager, which directly interfaces with the operating system (e.g., Winsock on Windows, POSIX sockets on Linux/macOS).  
The engine must bind a non-blocking UDP socket for all state transmission. TCP should be strictly reserved for out-of-band communication, such as connecting to login servers or downloading asset patches, and must never touch the gameplay state loop. To avoid heap allocations and garbage collection pauses, the socket layer must utilize fixed-size byte buffers (e.g., uint8\_t buffer\[2048\]). A ring buffer or object pool should be employed for retrieving and returning memory blocks, entirely bypassing dynamic allocation during the game loop.

### **Layer 2: Reliability and Sequencing Protocol**

Above the socket layer sits the ConnectionProtocol, analogous to Unity Transport's Pipeline stages. Because UDP is stateless, this layer must manually establish virtual connections using cryptographic handshake packets (Connection Request, Challenge, Accepted)3.  
Once a connection is established, the protocol appends a custom header to every outbound packet. A highly optimized C++ header struct requires minimal byte footprint:

C++  
struct PacketHeader {  
    uint16\_t sequenceId;          
    uint16\_t ackId;               
    uint32\_t ackBitmask;          
    uint8\_t  channelType;         
};

When the ConnectionProtocol receives a packet, it reads the ackBitmask. For every bit set to 1, it removes the corresponding packet from its internal "Pending Retransmission" queue. If a packet remains in the pending queue for longer than ![][image5] milliseconds, it is re-queued for transmission. This layer must also enforce MTU limits, explicitly fragmenting any payloads that exceed the defined threshold and managing reassembly on the receiving endpoint3.

### **Layer 3: The Replication Manager**

The ReplicationManager translates logical gameplay objects into bit streams. Every networked entity in the custom engine must be assigned a globally unique NetworkID.  
Following the Quake 3 delta compression methodology, the server maintains a SnapshotBuffer for each client—a circular array storing the serialized state of the world for the last 64 to 128 ticks. The replication loop executes as follows:

> 1. **Identify Baseline:** The server checks the ackId from the client's most recent packet to identify the last snapshot the client successfully processed22.  
> 2. **State Comparison:** The server compares the current state of every networked entity against its state in that specific baseline snapshot.  
> 3. **Generate Change Mask:** For each entity, a bitmask is constructed, flagging only the properties that have diverged from the baseline15.  
> 4. **Bit-Stream Serialization:** Using the custom BitWriter, the engine writes the entity's NetworkID, followed by the Change Mask, and subsequently packs only the quantized data for the flagged fields.

### **Layer 4: Simulation and Gameplay Integration**

The highest layer is the NetworkSimulation, which tethers the replication system to the physics and entity logic. Supporting client-side prediction requires profound modifications to the standard game loop.  
In a naive engine design, the update loop directly mutates object positions based on user input. In a networked engine, State and Logic must be completely decoupled35.

> * **State:** A struct representing the exact, immutable configuration of an entity at a given tick (Position, Velocity, Health, Animation Frame).  
> * **Input:** A struct representing the player's hardware inputs (buttons pressed, look vectors) at a specific tick.

The engine must feature a Simulate(State, Input, DeltaTime) \-\> State function. This function must be strictly deterministic; given the identical initial state and input, it must reliably yield the exact same resultant state across disparate CPU architectures32. The client uses this function to predictively march its local state forward, while utilizing the identical function during the rapid resimulation phase following a rollback28.

### **Critical Design Considerations**

When developing a proprietary networking stack, several common architectural errors will severely degrade simulation integrity:

> 1. **Over-relying on RPCs for State:** Remote Procedure Calls must be strictly reserved for transient events24. Utilizing reliable RPCs to update persistent values (such as entity health or inventory contents) will inevitably desynchronize late-joining clients, as RPCs do not persist in the baseline world state. All persistent data must be routed through the delta-compression replication manager.  
> 2. **Floating-Point Non-Determinism:** Compilers optimize floating-point arithmetic differently across architectures (e.g., x86 vs. ARM). Without strict IEEE 754 enforcement and fixed-point math libraries, the client and server physics simulations will drift, triggering endless prediction rollbacks30.  
> 3. **Prediction Tearing:** When restoring a buffer during rollback, an incorrect calculation of the array length will lead to memory overwriting, potentially destroying the predicted state of adjacent entities13. The simulation state must be deeply copied and verified during the resimulation loop.

The architecture of real-time multiplayer networking represents one of the most mathematically and structurally complex domains in computer science. Unity’s approach—splitting the low-level byte transmission from the high-level object synchronization—demonstrates the necessity of modularizing the stack. By embracing UDP with application-layer reliability, serializing data at the bit level, quantizing spatial mathematics, and mastering the complex timelines of client-side prediction, a custom engine can achieve the performance and bandwidth efficiency required to host massive, real-time interactive simulations.

#### **Trabalhos citados**

> 1. Transports | Netcode for GameObjects | 2.7.0 \- Unity \- Manual, [https\://docs.unity3d.com/Packages/com.unity.netcode.gameobjects@2.7/manual/advanced-topics/transports.html](https://docs.unity3d.com/Packages/com.unity.netcode.gameobjects@2.7/manual/advanced-topics/transports.html)  
> 2. Using pipelines | Unity Transport | 0.5.0-preview.5, [https\://docs.unity3d.com/Packages/com.unity.transport@0.5/manual/pipelines-usage.html](https://docs.unity3d.com/Packages/com.unity.transport@0.5/manual/pipelines-usage.html)  
> 3. realitydeslab/netcode-transport-multipeer-connectivity: This ... \- GitHub, [https\://github.com/realitydeslab/netcode-transport-multipeer-connectivity](https://github.com/realitydeslab/netcode-transport-multipeer-connectivity)  
> 4. Multiplay using Netcode for GameObjects | Extreal \- GitHub Pages, [https\://extreal-dev.github.io/Extreal.Guide/en/integration/multiplay.ngo/](https://extreal-dev.github.io/Extreal.Guide/en/integration/multiplay.ngo/)  
> 5. Using Netcode for GameObjects transports \- Unity \- Manual, [https\://docs.unity3d.com/Packages/com.unity.transport@6.6/manual/ngo-transports.html](https://docs.unity3d.com/Packages/com.unity.transport@6.6/manual/ngo-transports.html)  
> 6. Netcode for GameObjects | 3.1.0 \- Unity \- Manual, [https\://docs.unity3d.com/Packages/com.unity.netcode.gameobjects@latest/](https://docs.unity3d.com/Packages/com.unity.netcode.gameobjects@latest/)  
> 7. Unity 6000.7.0a5, [https\://unity.com/vn/releases/editor/alpha/6000.7.0a5](https://unity.com/vn/releases/editor/alpha/6000.7.0a5)  
> 8. Unity 6000.6.0f1, [https\://unity.com/releases/editor/whats-new/6000.6.0f1](https://unity.com/releases/editor/whats-new/6000.6.0f1)  
> 9. How do I manually accept a connection on the server using a, [https\://discussions.unity.com/t/how-do-i-manually-accept-a-connection-on-the-server-using-a-custom-networkinterface/917094](https://discussions.unity.com/t/how-do-i-manually-accept-a-connection-on-the-server-using-a-custom-networkinterface/917094)  
> 10. Netcode for GameObjects: transport seam and setup \- Hysen Labs, [https\://hysenlabs.com/projects/unity-technologies-com-unity-netcode-gameobjects](https://hysenlabs.com/projects/unity-technologies-com-unity-netcode-gameobjects)  
> 11. NetworkObject | Netcode for GameObjects | 2.4.4 \- Unity \- Manual, [https\://docs.unity3d.com/Packages/com.unity.netcode.gameobjects@2.4/manual/basics/networkobject.html](https://docs.unity3d.com/Packages/com.unity.netcode.gameobjects@2.4/manual/basics/networkobject.html)  
> 12. Справочник по Unity — Игры IT Universe, [https\://spirzen.ru/encyclopedia/9-spinoff/9-04-razrabotka-igr/301/](https://spirzen.ru/encyclopedia/9-spinoff/9-04-razrabotka-igr/301/)  
> 13. 2026-10-02 | Netcode for Entities | 7.1.0 \- Unity \- Manual, [https\://docs.unity3d.com/Packages/com.unity.netcode@7.1//changelog/CHANGELOG.html?ampDeviceId=a12d269d-d8e5-4215-afc0-ae4375bb9b43\&SessionId=1791331200499\&Timestamp=1791417600514](https://docs.unity3d.com/Packages/com.unity.netcode@7.1//changelog/CHANGELOG.html?ampDeviceId=a12d269d-d8e5-4215-afc0-ae4375bb9b43&ampSessionId=1791331200499&ampTimestamp=1791417600514)  
> 14. NetworkVariables | Netcode for GameObjects | 2.8.2 \- Unity \- Manual, [https\://docs.unity3d.com/Packages/com.unity.netcode.gameobjects@2.8/manual/basics/networkvariable.html](https://docs.unity3d.com/Packages/com.unity.netcode.gameobjects@2.8/manual/basics/networkvariable.html)  
> 15. NetworkVariableBase performing multiple WriteDelta calls?, [https\://discussions.unity.com/t/networkvariablebase-performing-multiple-writedelta-calls/908636](https://discussions.unity.com/t/networkvariablebase-performing-multiple-writedelta-calls/908636)  
> 16. NetworkManager | Netcode for GameObjects | 2.4.4 \- Unity \- Manual, [https\://docs.unity3d.com/Packages/com.unity.netcode.gameobjects@2.4/manual/components/networkmanager.html](https://docs.unity3d.com/Packages/com.unity.netcode.gameobjects@2.4/manual/components/networkmanager.html)  
> 17. Serialization | Netcode for GameObjects | 2.4.4 \- Unity \- Manual, [https\://docs.unity3d.com/Packages/com.unity.netcode.gameobjects@2.4/manual/serialization.html](https://docs.unity3d.com/Packages/com.unity.netcode.gameobjects@2.4/manual/serialization.html)  
> 18. Custom serialization | Netcode for GameObjects \- Unity \- Manual, [https\://docs.unity3d.com/Packages/com.unity.netcode.gameobjects@3.1/manual/advanced-topics/custom-serialization.html](https://docs.unity3d.com/Packages/com.unity.netcode.gameobjects@3.1/manual/advanced-topics/custom-serialization.html)  
> 19. 유니티 Netcode for GameObject, Synchronizing States & Events, [https\://wlsdn629.tistory.com/entry/%EC%9C%A0%EB%8B%88%ED%8B%B0-Netcode-for-GameObject-Synchronizing-States-Events](https://wlsdn629.tistory.com/entry/%EC%9C%A0%EB%8B%88%ED%8B%B0-Netcode-for-GameObject-Synchronizing-States-Events)  
> 20. Custom Config Syncing | Lethal Company Modding Wiki, [https\://lethal.wiki/dev/intermediate/custom-config-syncing](https://lethal.wiki/dev/intermediate/custom-config-syncing)  
> 21. (Netcode For Gameobjects) Getting "Reading past the end of the, [https\://discussions.unity.com/t/netcode-for-gameobjects-getting-reading-past-the-end-of-the-buffer-when-using-custom-messaging/865977](https://discussions.unity.com/t/netcode-for-gameobjects-getting-reading-past-the-end-of-the-buffer-when-using-custom-messaging/865977)  
> 22. Ghost snapshots | Netcode for Entities | 0.50.1-preview.19, [https\://docs.unity3d.com/Packages/com.unity.netcode@0.50/manual/ghost-snapshots.html](https://docs.unity3d.com/Packages/com.unity.netcode@0.50/manual/ghost-snapshots.html)  
> 23. Ghost snapshots and synchronization | Netcode for Entities | 1.4.1, [https\://docs.unity3d.com/Packages/com.unity.netcode@1.4/manual/ghost-snapshots.html](https://docs.unity3d.com/Packages/com.unity.netcode@1.4/manual/ghost-snapshots.html)  
> 24. Ghost snapshots | Netcode for Entities | 1.1.0-pre.3 \- Unity \- Manual, [https\://docs.unity3d.com/Packages/com.unity.netcode@1.1/manual/ghost-snapshots.html](https://docs.unity3d.com/Packages/com.unity.netcode@1.1/manual/ghost-snapshots.html)  
> 25. Network Serialization & Delta Compression \- FlushWhy Notes, [https\://notes.flushwhy.com/gamedev/networking/network-serialization-and-delta-compression/](https://notes.flushwhy.com/gamedev/networking/network-serialization-and-delta-compression/)  
> 26. Networking \- Structuring Data, [https\://research.ncl.ac.uk/game/mastersdegree/gametechnologies/networkingtutorials/2structuringdata/Networking%20-%20Structuring%20Data.pdf](https://research.ncl.ac.uk/game/mastersdegree/gametechnologies/networkingtutorials/2structuringdata/Networking%20-%20Structuring%20Data.pdf)  
> 27. Unreal Engine Console Variables Reference, [https\://dev.epicgames.com/documentation/unreal-engine/unreal-engine-console-variables-reference](https://dev.epicgames.com/documentation/unreal-engine/unreal-engine-console-variables-reference)  
> 28. Client-Side Prediction and Server Reconciliation \- Gabriel Gambetta, [https\://www\.gabrielgambetta.com/client-side-prediction-server-reconciliation.html](https://www.gabrielgambetta.com/client-side-prediction-server-reconciliation.html)  
> 29. Interpolation | Netcode for Entities | 1.3.2 \- Unity \- Manual, [https\://docs.unity3d.com/Packages/com.unity.netcode@1.3/manual/interpolation.html](https://docs.unity3d.com/Packages/com.unity.netcode@1.3/manual/interpolation.html)  
> 30. com.unity.netcode/Documentation\~/intro-to-prediction.md at master, [https\://github.com/needle-mirror/com.unity.netcode/blob/master/Documentation\~/intro-to-prediction.md](https://github.com/needle-mirror/com.unity.netcode/blob/master/Documentation~/intro-to-prediction.md)  
> 31. How does client-side prediction work?, [https\://gamedev.stackexchange.com/questions/22444/how-does-client-side-prediction-work](https://gamedev.stackexchange.com/questions/22444/how-does-client-side-prediction-work)  
> 32. Determinism \- Box2D, [https\://box2d.org/posts/2024/08/determinism/](https://box2d.org/posts/2024/08/determinism/)  
> 33. Some questions about Prediction and Interpolation \- Unity Discussions, [https\://discussions.unity.com/t/some-questions-about-prediction-and-interpolation/926330](https://discussions.unity.com/t/some-questions-about-prediction-and-interpolation/926330)  
> 34. Instance | Documentation \- Roblox Creator Hub, [https\://create.roblox.com/docs/reference/engine/classes/Instance](https://create.roblox.com/docs/reference/engine/classes/Instance)  
> 35. Add a multiplayer interface and visual nodes for SceneTree replication, [https\://github.com/godotengine/godot-proposals/issues/3459](https://github.com/godotengine/godot-proposals/issues/3459)

[image1]: <data:image/png;base64,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>

[image2]: <data:image/png;base64,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>

[image3]: <data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAEwAAAAVCAYAAADsFggUAAACAElEQVR4Xu2WDU0EMRBGVwMW0IAFLGABC1jAARKQgAMc4AADJwD2wX654WO2nXJHCElf0tx2tzOd3/aWZTKZTCaTyWQy+Ydcr+N2HRfb/MrmVfbkmPu7CpfruFmOsvwyr8J67MEuB90Cndmab6DweR132zis43Ed99sv36pg2NPyKfdi31639yOg720b6COp6OC3gnwjGOiIgUYPNgGB4nvJ14flqwFRkGcp7SHjAGOQFWSSOQkZQc5iH7LYQhCrkHTks/3RRXIFz6WAxay7Yn5LZbocWxFUpUKVUtWVgY6RYAHFAPjB/mpB9xNISgxgiXM4tlf+BPGnUCmjwYrQzvGIyPzE3tEO+IjwKY6BtyN4+Y+AvlOCpWoi6CJLIO/iJZDCuRMNQkl0jDKNG7G+d+ByDsSzICt/ILs9A7NgYV+8bdHR6gjsZf9ot9uod12kTDePZwIl0SmMzZyPuDGcI17+upU8yxHklEC1JM9eqaxx/RHtpSOCYCMTW1SXQxeCgTACOIlRce5GyPlWNpBBB2tZx7MHRvt65gXG69KQTaz16gIc90Q7Cr77yR4MvpfxNvN5Ritgqkh0oIvzK96YERzI9vJWZe7vIn50ZMivGHDmLb1noXWbEMh44JPJVru0Aj8CFbK3x5+iP6feFkJlDmSPOUHL4H2vKioQKD/XfoV3ayKc3gHASdEAAAAASUVORK5CYII=>

[image4]: <data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAMIAAAAVCAYAAAAU2jGmAAADrElEQVR4Xu2YgZHTQAxFXQMtUAMt0AItXAu0QAeUQAl0QAd0kAYoAPzm7nMa3TqXlbTxeuI3o0lix7b2S9rVellOTk5OTk6ifF7tx2rfV/vkzs3E02o/V/u22gd3bhaOoqUFLb8uz9ry+ZAQOBILSLQ/q318PT0N+IivQKL9Nudm4ShaeigAigFDV4r44SBwBEwghII5E7+W54ABAfu7vBbGLBxFSw9aaiX48vL74WDGYvDisswZPJJerQY+z1gIR9HSw+qlVpPvMxQC/uymHYl1hOUcgWZsjSxH0dLDyrtXa4RWxJbWF+12KUj1h9ENnu2PR4J/+BndLNu9xiiOoqWH9ogk3AsVAuOnIO9eCAQOAZRckUS5R/BILM1WiBaZbUcXwlG09NASYRDxuZryQmBQtgckmexvIHD0tvyXz0gQMsEjofGpldg6hr9slnmOnhUJWPQ6gR9oZH1VAsHeWrbiC9ZfztnXpFyD39JWLyX2pKwQGCw3Y8AYPReDVQ/GOeAcD7QWSZRo8AgCvnCt76nx8/LyneB4PyNkCgFf1bvySaKjn8a9t5YUAJr518t6uaCClZbyTWOSKTf2pKwQaCFsEOwA+a4EqyIaPHyiaAmSTxx8rJ6dMoVAguEn1/OJ7yN66oiWmvhAm00hbbVn4dNr3QvX8rwea634W/D/kkKwAdKMoOWQz+hGDhDBmzZa/jjml2mLen4SnsQX3udeeKb3A8NH7umPYz3Y/UoG7wMW0VItEWj1F74wgETL5MBoygrBgkB2RshAgvpKx1iKL43jmH233oLg4p9NrKzPPNP7geEjvvrj2K0zFveuKIIRWnIe3ez/uJdfWbnXzOBfeSEggp8RqmG26l3OxdPydqluzWIVZFojwNcR7ZAloyW+2QRqrawcy45ha8W9ZlurWYuSQuCB6meBhLIzQkboLTL35Do/6MvydharIFMIrSIgwd6bpXvJaKmVQ3AvP8lU+MxKjY89dvdC0ODVZ/LdCstDbm0DbiUTPK6zg9byHt0fXINnRQpBPqEd96AoaI8o2J4A30JGSyYP+8ZI2irx7aZ6ZkoKgSRnFUAEbkjQ7O9o332NTPAIDsEjiGqJEGGEn5FCUC+Pn/hE8uMfPo/wMaOl/ENLChU9MY6NjH8V6Nqy3pj9Rz3c1u9qMsHT6kSA1L8SuBFECgHt7KyvghhFRkuBf9ZHdO0d90mAqNBaunVta3NXCc+pbguriWp5cmDU0zLLkgB89xvSk5Nu/gHPIUfzMAujNQAAAABJRU5ErkJggg==>

[image5]: <data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAGEAAAAVCAYAAABWtYB0AAACPElEQVR4Xu2WDU3EQBCFqwELaMACFrCABSzgAAlIwAEOcIABBMB9tK+Zm8z+TcNBwn7JhmPb7sy+Nzvtskwmk8lkMvmz3J7GZ2W8nsb9fvfK8zbfM7gXroJrtfGwPnZxiHvtJys8LauGgt/MpUAsRLcLwuM2r4VJ8GNZjUFYzXHP2/Y/cO1lWQUF7uf63X7HOmfXBtZ6X9a4l4JY5EDcSIMavmgZ6dxJAHEjtDhQKb5KJaYPzmY0hyE35hrUjPdzPwnxKA6MiPKpoW7BYD8jz56hSkYoD8JxTQYRTCdAlMTELJ0YnQiLKs9DHq2W4A318LzPs4VOvd9HjSj/FKpkX+GgxFTRvtqhJCbrIhbDtiGQ8ZE5PT0V41k/gnhRsbT4VRNUyb66MMUaEFETs0aphY0QGZE1ALIm8BwxeeeRUyb2/j5gMYYqu6ctZMUstbBRrBFHDICsCfZ+NMOMoRxK74PehLJillpYBnJQNQ5t3tG7Z4vvHjzLGlFrL1KqZC3W6s8ZMbMtrARCcJKHNh6QMcEj3Yb2VqrkkjmWrJg9a/diW1D0jhhh1AR90trTkDKhVMkypyZUz4s7gtY3stkS0TvgiBEtE5i3sYhdMqHVQXZqlawAEpi/PrmsmLSOyPgRIgNE1oiaCSo4++7kft8Cic3+Wh8030EkhB02cb7rFZR5BdcJ8YP1au7rxPnBfEaw1qcguTSF2FDB+WFPuPTweyQPfRjIAP+yPgSbQKCMSP8JRMcEjKoVxmQyOeMLaV31uRokWUgAAAAASUVORK5CYII=>
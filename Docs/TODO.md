Context:

There is already a bridge that connects the editor and the engine, but there are issues and so i want to reinvent the scene logic (if necessary), and also add the rest of the entities/gameobjects (like for example decals) and scripts for making things happen (usual components of a game editor). And then when there is a more structured scene logic fix the issues of adding objects to the scene.
Current viewport doesnt have the logic for the side buttons on the editor (Select, Move, Rotate, etc) and still needs to turn on the editor on the HelperSuite to show the gizmo.

Features I want to implement:

- Scene data file with custom extension for easy identification
- Create multiple scenes
- Editor Camera system (separate from game cameras) that handles mouse and keyboard inputs to manipulate the view matrix.
    - Right-click + Mouse Drag: Rotates/looks around (Pitch and Yaw, with clamping to prevent flipping).
    - Middle-click + Mouse Drag: Pans the camera horizontally and vertically relative to its orientation.
    - Scroll Wheel: Zooms in/out along the camera's forward vector.
    - Calculate and update the View Matrix based on these transformations.
- After having the editor camera system, implement the rest of the features for moving and rotating.
- Play/Stop button logic

Additional notes:

THE SCREEN-TO-WORLD RAYCAST (Object Selection)
Write a function that translates a 2D mouse click inside the viewport screen boundaries into a 3D ray.
- Inputs: `(int mouseX, int mouseY)`, `int viewportWidth`, `int viewportHeight`, the `ProjectionMatrix`, and the `View Matrix`.
- Convert screen pixels to Normalized Device Coordinates (NDC) ranging from -1 to 1.
- Unproject the NDC into World Space using the inverse View-Projection matrix to calculate the Ray Origin and normalized Ray Direction.
- Provide a basic Ray-vs-AABB (Axis-Aligned Bounding Box) intersection function so the editor can detect which object was clicked.

THE RUNTIME EDIT/PLAY LOOP HOOK
- Provide a structured loop or update function that shows how this viewport processes inputs, updates the camera, and renders the scene texture inside the UI framework window frame every frame.

Current known Issues:

- Inspector still closes when trying to interact with the input boxes for the values, sliders and color pickers
- Tried diagnosing the Add Object crash issue but the log file doesnt show or create
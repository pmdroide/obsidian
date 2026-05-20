# Vista UI Engine: Architectural Summary

## Overview
The Vista UI system is a lightweight, CSS-driven layout engine designed for MonoGame. It decouples UI structure and styling from gameplay logic, enabling dynamic, resolution-independent interfaces using standard XML and CSS.

---

## Architecture Structure

The system is built on a **DOM-to-Visual Translation Pipeline**:

### 1. The Parser (AngleSharp) - "The Brain"
* **Function:** Interprets XML structure and CSS styling.
* **Role:** It builds a standard DOM tree and computes styles, resolving relative units (e.g., `50%`, `rem`) into absolute pixel values based on the `VistaRenderDevice`.

### 2. The Visual Node Tree (`UIElement.cs`) - "The Skeleton"
* **Function:** Maintains a 1-to-1 mirror of the DOM.
* **Role:** Each element calculates its own `ScreenBounds` (Rectangle) recursively, allowing for complex parent-child positioning without manual coordinate math.

### 3. The Orchestrator (`UIManager.cs`) - "The Heart"
* **Function:** Manages the lifecycle and rendering bridge.
* **Role:** Holds the `IBrowsingContext`, handles file I/O, manages the update/draw loops, and provides an interface for game logic to interact with the UI (e.g., `SetText`).

---

## Execution Flow

1.  **Initialization:** The `UIManager` boots the AngleSharp context with `VistaRenderDevice`, which acts as a virtual monitor to resolve CSS metrics.
2.  **Update Pass:** The system triggers `CalculateLayout()`. The UI traverses the tree, calculating the absolute screen coordinates for every element based on its computed style.
3.  **Draw Pass:** The `Draw()` method iterates through the calculated `UIElements` and uses MonoGame’s `SpriteBatch` to render backgrounds and text content to the screen.

---

## Integration Patterns
* **State Sync:** Use `UIManager.SetText()` or modify DOM attributes in your game loop to reflect real-time gameplay changes.
* **Performance:** Updates are throttled (e.g., checking FPS/Memory every 500ms) to ensure minimal CPU overhead for DOM manipulation.
* **Layout:** Leverage CSS absolute positioning (`top`, `right`, `width`, `height`) within XML/CSS files to handle responsive UI scaling automatically.

[Image of software architecture diagram showing UI parsing logic flow from XML to MonoGame rendering]

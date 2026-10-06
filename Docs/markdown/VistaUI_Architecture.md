# Vista UI

Vista draws XML/CSS documents with MonoGame's `SpriteBatch`. AngleSharp parses the document and
computes the CSS; Vista mirrors the DOM as a tree of `UIElement`s, lays it out, and draws it.

It is used in two places:

- **The debug overlay** (`Content/UI/debug.xml` + `.css`), drawn on top of everything by `ScreenManager`.
  Toggle it with `GameSettings.ui_vista_enabled`.
- **Game UI layers** opened by scripts with `GameUI.Open("UI/MainMenu")`: menus and HUDs, drawn over
  the scene and under the debug overlay. The MainMenu sample scene uses one; see
  [Scenes_and_Game_Flow.md](Scenes_and_Game_Flow.md).

## Pieces

| File | Role |
| --- | --- |
| `Vista/UI/UIManager.cs` | One document: loading, the element tree, restyling, layout, drawing, hit testing, gradient textures. |
| `Vista/UI/UIElement.cs` | One element: layout box, transitions, background/gradient/border/text drawing, hit testing. |
| `Vista/UI/UIComputedStyle.cs` | The CSS one element uses, parsed once from AngleSharp's styles. |
| `Vista/UI/UIStyles.cs` | Parsers: lengths, colours, times, gradients, `text-shadow`. |
| `Vista/UI/UIFontRegistry.cs` | CSS `font-family` names → `SpriteFont`s. Shared by every document. |
| `Engine/Logic/GameUI.cs` | Script-facing layers: open/close, update, draw, hot reload. |

## Frame flow

1. **Restyle.** Only elements marked dirty are recomputed. A computed style costs about 0.15 ms, so
   Vista caches styles instead of recomputing every element each frame.
2. **Layout.** `UIManager.Update` lays the tree out inside the viewport (or the virtual canvas, see
   *Scaling*), advancing transitions by the frame time.
3. **Draw.** `UIManager.Draw` draws in document order: later elements are on top.

Changes made through `UIManager` restyle the affected subtree automatically: `SetClass`, `SetVariable`.
`SetText` needs no restyle. After editing the DOM directly, call:

- `Invalidate(element)` after changing an element's classes, attributes or inline `style`;
- `Refresh()` after adding, removing or moving elements. Existing elements keep their transition state.

## Layout model

Every element is positioned absolutely inside its parent's box. There is no flow layout: stack rows by
giving them `top` values (the MainMenu script sets an inline `style="top: …px"` per row).

| Property | Behaviour |
| --- | --- |
| `left` / `top` | Offset from the parent's left/top edge. px, %, em/rem (16 px). |
| `right` / `bottom` | Anchor to the parent's right/bottom edge when `left`/`top` is not set. |
| `width` / `height` | Size. When not set: `left` + `right` (or `top` + `bottom`) stretch between them; otherwise the parent's full size. |
| `margin-*` | Extra offset. Percentages use the parent's width. Centre a 200 px box with `left: 50%; margin-left: -100px`. |
| `padding-*` | Insets the text only (px). Children are positioned from the border edge, not the padding edge. |

Percentages are always relative to the **parent box**. AngleSharp resolves them against its render
device in the computed style, which is wrong for nested boxes and scaled canvases, so Vista reads box
geometry from the cascaded (declared) style instead.

## Supported CSS

| Area | Properties |
| --- | --- |
| Visibility | `display: none` (removed from layout, drawing and hit testing), `visibility: hidden`, `opacity` (multiplies down the tree), `pointer-events: none` |
| Background | `background-color`; `background-image: linear-gradient(...)` and `radial-gradient(...)` |
| Border | `border`, `border-<side>`, `border-<side>-width/-color/-style` (solid fills; no radius) |
| Text | `color`, `font-family`, `text-align` (left/center/right), `vertical-align` (top/middle/bottom), `letter-spacing`, `line-height` (px or a multiplier), `text-transform` (uppercase/lowercase), `white-space` (`nowrap`, `pre-line` keeps line breaks), `text-shadow` (first shadow; blur is approximated) |
| Motion | `transition-property` / `transition-duration` (or the `transition` shorthand) for `opacity`, `background-color`, `color`, `border-*-color`, `left/top/right/bottom` and `width/height` |

Details:

- **Text** wraps at word boundaries to the content box, unless `white-space: nowrap`. Whitespace is
  collapsed as in a browser. Only an element's own text nodes are drawn; text in children is drawn by
  the children.
- **`vertical-align: middle`** centres the capital letters, not the line box, so single-line labels
  in buttons look centred.
- **Gradients**: linear angles snap to the nearest axis (to top/right/bottom/left). Colour stops are
  spaced evenly; stop positions are ignored. Repeat a stop to hold a colour
  (`transparent, transparent, black`). A radial gradient is an ellipse reaching the corners.
- **Transitions** ease out towards the new value, covering about 99% of the change in the duration.
  Timing functions and delays are ignored. Nothing animates on the first layout, or when an element
  reappears after `display: none`, so fade elements in with an `opacity` class instead.
- **Colours**: `#rgb`, `#rgba`, `#rrggbb`, `#rrggbbaa`, `rgb()`/`rgba()` and a few names. Vista
  premultiplies them for SpriteBatch's blending.
- **Fonts** are whatever the host registered. Characters missing from a font are drawn as `?`
  instead of throwing.

## Fonts

`ScreenManager.LoadVistaUI` registers:

| `font-family` | SpriteFont | Use |
| --- | --- | --- |
| `default` | `Fonts/defaultfont` (Arial 10) | Fallback when no family matches. |
| `monospace` | `Fonts/monospace` (Lucida Console 10) | Debug overlay. |
| `display` | `Fonts/UI/Display` (Bahnschrift, about 88 px) | Titles. |
| `heading` | `Fonts/UI/Heading` (Bahnschrift, about 32 px) | Menu items, screen titles. |
| `caption` | `Fonts/UI/Caption` (Bahnschrift, about 16 px) | Small caps labels (use `letter-spacing`). |
| `body` | `Fonts/UI/Body` (Segoe UI, about 19 px, includes ← ↑ → ↓) | Paragraphs, key hints. |

The UI fonts are sized for a 1080-high canvas. SpriteFonts don't scale cleanly, so pick a family
rather than a `font-size` (`font-size` is ignored). To add a face, add a `.spritefont` under
`Content/Fonts/UI`, add it to `Content.mgcb`, and register it in `ScreenManager.LoadVistaUI`.

## Scaling

`UIManager.ReferenceHeight` sets a design height. With `ReferenceHeight = 1080`, the document is laid
out on a canvas 1080 units high (and as wide as the window's aspect allows), then drawn scaled to the
window. `GameUI.Open` uses 1080 by default; the debug overlay uses real pixels. Draw a scaled document
inside `spriteBatch.Begin(transformMatrix: ui.Transform)`. `ElementAt` takes viewport pixels and does
the conversion itself.

## Hit testing

`ui.ElementAt(point)` returns the topmost visible element under a viewport position. Elements that
are `display: none`, hidden, nearly transparent (opacity below 0.05) or `pointer-events: none` are
skipped; their children can still be hit. Find the interactive ancestor with
`hit.DomNode.Closest(".option")`.

## Scripting a document

```csharp
UIManager ui = GameUI.Open("UI/MyMenu");        // Content/UI/MyMenu.xml + .css
ui.SetClass("#settings", "active", true);       // restyles, transitions run
ui.SetText("#score", score.ToString());
ui.Get("#press").RuntimeOpacity = pulse;        // per-frame animation without restyling
ui.Get("#panel").RuntimeOffset = new Vector2(0, shake);
var hit = ui.ElementAt(GameInput.MousePosition)?.DomNode.Closest(".button");
GameUI.Close(ui);
```

`RuntimeOpacity` and `RuntimeOffset` are applied on top of the CSS without touching the DOM. Use them
for continuous animation (a pulsing prompt), which would otherwise need a restyle every frame.

## Gotchas

- **The document is parsed as HTML.** Write `<div class="hl"></div>`, never `<div class="hl"/>`:
  HTML has no self-closing `div`, so the following elements would end up inside it.
- **AngleSharp.Css can't compute `radial-gradient`** (it throws a `NullReferenceException`). Vista
  rewrites radial gradients to a marked `linear-gradient(0.5deg, …)` when it loads a stylesheet and
  reads them back as radial, so write `radial-gradient(...)` as usual.
- If a style still fails to compute, that element and its children are hidden (and a line is written
  to the debug output) instead of crashing the game.
- Browsers give `<body>` an 8 px margin; Vista adds `html, body { margin: 0; padding: 0; }` before your
  stylesheet.
- `GameUI` documents are loose files read from the source `Engine/Content` in a dev checkout. Saving the
  XML or CSS while the game runs reloads the layer within half a second. Scripts that build parts of the
  document should rebuild them on `UIManager.Loaded`.

# Blender

Moved from `CLAUDE.md`; read when the task touches this area.

Blender is available through the Blender MCP server.

When performing Blender work:

1. Inspect the current scene before making changes.
2. Use Blender MCP instead of generating scripts for me to manually run.
3. Do not delete or modify unrelated objects.
4. Make changes incrementally.
5. Inspect the result after major changes.
6. Use viewport screenshots when useful.
7. Preserve existing object names unless renaming is necessary.
8. Save the Blender file only when explicitly requested.

### Game Assets

For game-ready meshes:

- Keep topology reasonably efficient.
- Apply transforms before export.
- Check normals.
- Check for non-manifold geometry.
- UV unwrap meshes when appropriate.
- Use sensible real-world scale.
- Set sensible object origins.
- Avoid unnecessary modifiers before export.


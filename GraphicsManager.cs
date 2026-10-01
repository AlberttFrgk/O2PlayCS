using System;
using Raylib_cs;
using Rectangle = Raylib_cs.Rectangle;

namespace O2Play
{
    // Master Graphics Coordinator orchestrating PlayfieldRenderer and SidebarRenderer.
    public class GraphicsManager : IDisposable
    {
        private readonly GameEngine? _engine;

        public PlayfieldRenderer Playfield { get; } = new();
        public SidebarRenderer Sidebar { get; } = new();

        public GraphicsManager() { }

        public GraphicsManager(GameEngine engine)
        {
            _engine = engine;
        }

        // Forward layout constants for full backward compatibility
        public const int HitY = PlayfieldRenderer.HitY;
        public const int NoteHeight = PlayfieldRenderer.NoteHeight;
        public const int KeyTopY = PlayfieldRenderer.KeyTopY;

        public static readonly int[] LaneX = PlayfieldRenderer.LaneX;
        public static readonly int[] LaneW = PlayfieldRenderer.LaneW;

        public static readonly Rectangle[] KeySrcRects = PlayfieldRenderer.KeySrcRects;
        public static readonly Rectangle[] KeyDestRects = PlayfieldRenderer.KeyDestRects;
        public static readonly Rectangle[] LightSrcRects = PlayfieldRenderer.LightSrcRects;
        public static readonly Rectangle[] LightDestRects = PlayfieldRenderer.LightDestRects;

        public static readonly int[] ComboGlyphLeft = PlayfieldRenderer.ComboGlyphLeft;
        public static readonly int[] ComboGlyphWidth = PlayfieldRenderer.ComboGlyphWidth;
        public static readonly int[] ComboGlyphAdvance = PlayfieldRenderer.ComboGlyphAdvance;

        public void InvalidateLowerPanel() => Sidebar.InvalidateLowerPanel();

        public void Reset() => Sidebar.Reset();

        public void Draw()
        {
            if (_engine != null)
            {
                Draw(_engine);
            }
        }

        public void Draw(GameEngine engine)
        {
            Playfield.Draw(engine);
            Sidebar.Draw(engine);
        }

        public void DrawNumber(Texture2D tex, int number, int x, int y)
        {
            Playfield.DrawNumber(tex, number, x, y);
        }

        public void Dispose()
        {
            Sidebar.Dispose();
        }
    }

    // Merged alias for backward compatibility
    public class GameInterface : GraphicsManager
    {
        public GraphicsManager Graphics => this;

        public GameInterface(GameEngine engine) : base(engine) { }
    }
}

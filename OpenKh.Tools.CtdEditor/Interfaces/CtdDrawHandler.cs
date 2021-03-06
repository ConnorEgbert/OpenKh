﻿using OpenKh.Bbs;
using OpenKh.Bbs.Messages;
using OpenKh.Tools.Common;
using System.Drawing;
using System.Linq;
using Xe.Drawing;

namespace OpenKh.Tools.CtdEditor.Interfaces
{
    public class CtdDrawHandler : IDrawHandler
    {
        private const int PspScreenWidth = 480;
        private const int PspScreenHeight = 272;

        public CtdDrawHandler()
        {
            DrawingContext = new DrawingDirect3D();
        }

        public IDrawing DrawingContext { get; }

        public void DrawHandler(
            ICtdMessageEncoder encoder,
            FontsArc.Font fontContext,
            Ctd.Message message,
            Ctd.Layout layout)
        {
            DrawPspScreen();
            DrawDialog(layout);

            int BeginX = layout.DialogX + layout.TextX;
            int BeginY = layout.DialogY + layout.TextY;
            var x = BeginX;
            var y = BeginY;
            var texture1 = DrawingContext.CreateSurface(fontContext.Image1);
            var texture2 = DrawingContext.CreateSurface(fontContext.Image2);
            foreach (var ch in encoder.ToUcs(message.Data))
            {
                if (ch >= 0x20)
                {
                    var chInfo = fontContext.CharactersInfo.FirstOrDefault(info => info.Id == ch);
                    if (chInfo == null)
                    {
                        if (ch == 0x20)
                            x += fontContext.Info.CharacterWidth / 2;
                        continue;
                    }
                    if (chInfo.Palette >= 2) continue;

                    var texture = chInfo.Palette == 0 ? texture1 : texture2;
                    var source = new Rectangle
                    {
                        X = chInfo.PositionX,
                        Y = chInfo.PositionY,
                        Width = chInfo.Width,
                        Height = fontContext.Info.CharacterHeight
                    };
                    DrawingContext.DrawSurface(texture, source, x, y);

                    x += source.Width + layout.HorizontalSpace;
                }
                else
                {
                    switch (ch)
                    {
                        case 0x0a: // '\n'
                            x = BeginX;
                            y += 16 + layout.VerticalSpace;
                            break;
                    }
                }
            }
        }

        private void DrawPspScreen() =>
            DrawingContext.FillRectangle(new RectangleF(0, 0, PspScreenWidth, PspScreenHeight), Color.Black);

        private void DrawDialog(Ctd.Layout layout) => DrawingContext.DrawRectangle(new RectangleF
        {
            X = layout.DialogX - 1,
            Y = layout.DialogY - 1,
            Width = layout.DialogWidth + 1,
            Height = layout.DialogHeight + 1,
        }, Color.Cyan);
    }
}

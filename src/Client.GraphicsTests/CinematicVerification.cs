using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FrogSmashers.Client.GraphicsTests;

internal sealed partial class PresentationChecks
{
    public void CaptureCinematics(string directory)
    {
        Directory.CreateDirectory(directory);
        using var cinema = new CinematicPlayer(device, assets);
        void Capture(string name)
        {
            Texture2D frame = cinema.Draw();
            device.SetRenderTarget(null);
            using var stream = File.Create(Path.Combine(directory, name + ".png"));
            frame.SaveAsPng(stream, frame.Width, frame.Height);
        }

        void Advance(string name, int[] frames)
        {
            int current = 0;
            foreach (int frame in frames)
            {
                while (current < frame)
                {
                    cinema.Update(1 / 60f);
                    current++;
                }

                Capture($"{name}-{frame:0000}");
            }
        }

        cinema.StartIntro();
        Advance("intro", [0, 300, 900, 1200, 1260, 1380, 1590, 1800]);
        cinema.StartOutro(PlayerColors[2]);
        Advance("outro", [0, 90, 300, 600, 900, 1200, 1500]);
        cinema.ShowJoin();
        Advance("join", [0, 60]);
    }
}

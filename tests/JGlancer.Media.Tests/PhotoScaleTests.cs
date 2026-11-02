using JGlancer.Media.Photos;

namespace JGlancer.Media.Tests;

public sealed class PhotoScaleTests
{
    [Fact]
    public void APhotoFitsTheScreenInItsBetterOrientation()
    {
        // 12 MP 4:3 on an ultrawide: the height decides (1920 × 1440), not the screen's long side.
        Assert.Equal(0.48, PhotoScale.ToFit(4000, 3000, 3440, 1440), 3);
        // A portrait photo fits upright at 0.36, turned at 0.48: the decode stays sharp after a view rotation.
        Assert.Equal(0.48, PhotoScale.ToFit(3000, 4000, 3440, 1440), 3);
        // Never upscaled; nothing to fit gives 1.
        Assert.Equal(1, PhotoScale.ToFit(640, 480, 3440, 1440));
        Assert.Equal(1, PhotoScale.ToFit(0, 0, 3440, 1440));
        Assert.Equal(1, PhotoScale.ToFit(9000, 6000, IPhotoDecoder.Unbounded, IPhotoDecoder.Unbounded));
    }
}

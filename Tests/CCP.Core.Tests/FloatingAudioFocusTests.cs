using ConditioningControlPanel.Services.Video;
using Xunit;

namespace CCP.Core.Tests;

public class FloatingAudioFocusTests
{
    [Fact]
    public void Newest_or_touched_window_holds_sound_and_close_hands_it_on()
    {
        var focus = new FloatingAudioFocus<object>();
        var a = new object(); var b = new object(); var c = new object();
        focus.Join(a, false); focus.Join(b, false); focus.Join(c, false);
        Assert.Same(c, focus.Holder);
        Assert.True(focus.Touch(a));
        Assert.Same(a, focus.Holder);
        Assert.True(focus.Remove(a));
        Assert.Same(c, focus.Holder);
        Assert.True(focus.Remove(c));
        Assert.Same(b, focus.Holder);
        Assert.True(focus.Remove(b));
        Assert.Null(focus.Holder);
    }

    [Fact]
    public void Keep_sound_joins_behind_the_holder()
    {
        var focus = new FloatingAudioFocus<object>();
        var a = new object(); var b = new object(); var c = new object();
        focus.Join(a, false); focus.Join(b, true); focus.Join(c, true);
        Assert.Same(a, focus.Holder);
        Assert.True(focus.Remove(a));
        Assert.Same(c, focus.Holder);
        Assert.True(focus.Touch(b));
        Assert.Same(b, focus.Holder);
    }
}

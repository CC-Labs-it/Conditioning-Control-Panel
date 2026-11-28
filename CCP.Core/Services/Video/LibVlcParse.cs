using System.Threading.Tasks;
using LibVLCSharp.Shared;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The one way to await a LibVLCSharp <see cref="Media.Parse"/> in this app.
    /// </summary>
    /// <remarks>
    /// LibVLCSharp completes the parse task inside libvlc's own ParsedChanged callback, on
    /// libvlc's thread, while libvlc is still walking that media's event listeners. A caller with
    /// no synchronization context (anything after a ConfigureAwait(false), any Task.Run) then runs
    /// its continuation right there, and disposing the media in it frees the event manager under
    /// libvlc: native heap corruption that surfaces later as a 0xC0000005 at ntdll+0x1A12D in an
    /// unrelated allocation (proven on the Avalonia head, restack fixw4c #5071). This helper only
    /// returns once execution has left that thread, on the success path and the throwing one, so
    /// the caller may read, stop or dispose the media afterwards. Never touch a Media inside a
    /// libvlc event handler.
    /// </remarks>
    internal static class LibVlcParse
    {
        internal static async Task<MediaParsedStatus> ParseOffCallbackAsync(
            Media media, MediaParseOptions options, int timeoutMs)
        {
            MediaParsedStatus status;
            try
            {
                status = await media.Parse(options, timeoutMs).ConfigureAwait(false);
            }
            catch
            {
                await Task.Yield();
                throw;
            }
            // No context on libvlc's thread, so the yield queues the rest to the thread pool.
            await Task.Yield();
            return status;
        }
    }
}

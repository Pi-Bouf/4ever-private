// Where the tool keeps its own bookkeeping (what a port added, the merge marker): Tools/ClassicPort/state/,
// git-ignored. Never inside Game/ — the client folder holds only the exe and game data.
namespace ClassicPort;

public static class StateFiles
{
    public static string Dir = "";
    public static string Get(string name) { Directory.CreateDirectory(Dir); return Path.Combine(Dir, name); }
}

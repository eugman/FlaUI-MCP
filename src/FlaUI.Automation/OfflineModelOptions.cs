using System.Text;

/// <summary>Run-local user options; never changes global preferences or an existing options file.</summary>
public static class OfflineModelOptions
{
    public static string Create(string modelPath, string outputRoot, string userName)
    {
        modelPath = ArtifactFiles.ContainedPath(modelPath, outputRoot);
        if (!File.Exists(modelPath) || !string.Equals(Path.GetExtension(modelPath), ".bim", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Offline options require an existing run-local BIM");
        if (string.IsNullOrWhiteSpace(userName) || userName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("Invalid user name for model options filename");
        var destination = Path.Combine(Path.GetDirectoryName(modelPath)!, Path.GetFileNameWithoutExtension(modelPath) + "." + userName + ".tmuo");
        ArtifactFiles.ContainedPath(destination, outputRoot);
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write);
        output.Write(Encoding.UTF8.GetBytes("{\"UseWorkspace\":false}"));
        output.Flush(true);
        return destination;
    }
}

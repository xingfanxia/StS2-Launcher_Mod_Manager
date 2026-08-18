using GameContract;

public class ConsumerFileIo : IFileIo
{
    public string[] GetFiles(string path) => [];

    // The old compile-time contract does not require this yet, so it must be
    // virtual to remain bindable if a newer runtime adds the interface slot.
    public virtual void CopyFile(string source, string destination) { }
}

namespace GameContract;

public interface IFileIo
{
    string[] GetFiles(string path);
    void CopyFile(string source, string destination);
}

public class PatchTarget
{
    public void Present() => Helper();

    public void Overloaded() { }

    public void Overloaded(int value) { }

    private void Helper() { }
}

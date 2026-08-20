using System;
using System.IO;

namespace STS2Mobile.Steam;

// Writes a sibling temporary file and publishes it with one same-directory
// rename. A pre-publish failure leaves the existing destination untouched and
// never deletes it; a later retry safely overwrites the temporary file.
public static class NonDestructiveFileTransaction
{
    public static bool TryWriteAtomic(
        string destination,
        string contents,
        out string failureType,
        Action beforePublish = null
    )
    {
        failureType = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var temporary = destination + ".tmp";
            File.WriteAllText(temporary, contents);
            beforePublish?.Invoke();
            File.Move(temporary, destination, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            failureType = ex.GetType().Name;
            return false;
        }
    }
}

namespace Nick.Plugin.Jellyscrub.Conversion;

/// <summary>
/// Shared task for conversion and deletion of BIF files. 
/// </summary>
public class ConversionTask
{
    private readonly PrettyLittleLogger _convertLogger = new PrettyLittleLogger();
    private readonly PrettyLittleLogger _deleteLogger = new PrettyLittleLogger();

    private bool _busy = false;
    private readonly object _lock = new object();


    public void ConvertAll()
    {
        if (!CheckAndSetBusy(_convertLogger)) return;

        _convertLogger.ClearSynchronized();
        for (int i = 0; i < 5; i++)
        {
            Thread.Sleep(5000);
            _convertLogger.LogSynchronized($"Convert message {i}", PrettyLittleLogger.LogColor.Blue);
        }

        _busy = false;
    }

    public void DeleteAll()
    {
        if (!CheckAndSetBusy(_deleteLogger)) return;

        _busy = false;
    }

    public string GetConvertLog()
    {
        return _convertLogger.ReadSynchronized();
    }

    public string GetDeleteLog()
    {
        return _deleteLogger.ReadSynchronized();
    }

    private bool CheckAndSetBusy(PrettyLittleLogger logger)
    {
        lock (_lock)
        {
            if (_busy)
            {
                logger.LogSynchronized("[!] Already busy running a task.", PrettyLittleLogger.LogColor.Red);
                return false;
            }
            _busy = true;
            return true;
        }
    }
}

using System;
using System.Threading.Tasks;

namespace StarterNG.Application;

/// <summary>
/// Runs a piece of work one at a time. Asked again while it runs, it does not start a
/// second one alongside, nor forget the request: it runs once more when the first ends,
/// however many times it was asked meanwhile. For the UI thread, which calls it alone.
/// </summary>
public sealed class OneAtATime
{
    private readonly Func<Task> _work;
    private bool _running;
    private bool _again;

    public OneAtATime(Func<Task> work) => _work = work;

    public bool IsRunning => _running;

    public async Task RunAsync()
    {
        if (_running)
        {
            _again = true;
            return;
        }

        _running = true;
        try
        {
            do
            {
                _again = false;
                await _work();
            }
            while (_again);
        }
        finally
        {
            _running = false;
            _again = false;
        }
    }
}

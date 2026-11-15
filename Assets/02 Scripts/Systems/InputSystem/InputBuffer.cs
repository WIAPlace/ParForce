using System.Collections.Generic;

// Will be used for inputs to be queued and buffered. 
public class InputBuffer<T> where T : struct
{
    private struct BufferedItem
    {
        public T Action;
        public int ExpireTick;
    }

    private BufferedItem? _bufferedItem;
    private readonly int _bufferDurationTicks;

    // If tick rate is 60Hz, 12 ticks = 0.2 seconds buffer window
    public InputBuffer(int bufferDurationTicks = 12)
    {
        _bufferDurationTicks = bufferDurationTicks;
    }

    public void Add(T action, int currentTick)
    {
        _bufferedItem = new BufferedItem
        {
            Action = action,
            ExpireTick = currentTick + _bufferDurationTicks
        };
    }

    public bool TryConsume(int currentTick, out T action)
    {
        if (_bufferedItem.HasValue && currentTick <= _bufferedItem.Value.ExpireTick)
        {
            action = _bufferedItem.Value.Action;
            _bufferedItem = null;
            return true;
        }

        _bufferedItem = null;
        action = default;
        return false;
    }

    public void ClearBuffer() => _bufferedItem = null; 
}
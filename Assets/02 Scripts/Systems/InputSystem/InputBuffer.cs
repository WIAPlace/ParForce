using System.Collections.Generic;
using System.Diagnostics;

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
    public bool Check()=> _bufferedItem!=null;
}

public class ChainBuffer
{
    private struct BufferedItem
    {
        public int ChainCount;
        public int CapturedTick;
    }

    private BufferedItem _bufferedItem;
    private readonly int _bufferDurationTicks;

    // If tick rate is 60Hz, 12 ticks = 0.2 seconds buffer window
    public ChainBuffer(int bufferDurationTicks = 12)
    {
        _bufferDurationTicks = bufferDurationTicks;
        Reset();
    }

    // fully go to the next choice.
    public int Step(int maxLength,int currentTick)
    {
        int evalationTick = currentTick - _bufferDurationTicks;

        if(evalationTick <= _bufferedItem.CapturedTick && _bufferedItem.ChainCount < maxLength)
        {
            int tempCount = _bufferedItem.ChainCount;
            _bufferedItem = new BufferedItem
            {
                ChainCount = (tempCount+1)%maxLength,
                CapturedTick = currentTick
            };
        }
        else
        {
            _bufferedItem = new BufferedItem
            {
                ChainCount = 0,
                CapturedTick = currentTick
            };
        }
        return _bufferedItem.ChainCount;
    }

    // Soft checker for when we dont want to step.
    public int CheckCount()
    {
        return _bufferedItem.ChainCount;
    }
    public int CheckTick()
    {
        return _bufferedItem.CapturedTick;
    }

    public void Reset()
    {
        _bufferedItem = new BufferedItem
        {
            ChainCount = 0,
            CapturedTick = 0 // this will immediatly flag to start from 0, the first in the array.
        };
    }
} 
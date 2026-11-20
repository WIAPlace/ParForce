using UnityEngine;

public enum KeyPress
{
    LeftClick,
    RightClick,
}

// might need to have a seprate finite state machine for this.
// To keep track of what can and cant be done. rather than an f tone of ifs
public struct ActionStructure
{
    public ActionOrder order;
    public int state;
}

namespace Systems.Inventory.Architecture
{
    /// <summary>
    /// Presentation contract consumed by InventoryPanelController.
    /// A UI Toolkit view can implement this without the model knowing about UI Toolkit.
    /// </summary>
    public interface IInventoryPanelView
    {
        void Render(IInventoryModel model);
        void Clear();
    }
}

using System;

namespace Systems.Inventory.Architecture
{
    /// <summary>
    /// Owns the binding lifecycle for one visible inventory panel.
    /// Create one controller per panel; multiple controllers can bind to
    /// different models at the same time.
    /// </summary>
    public sealed class InventoryPanelController : IDisposable
    {
        private readonly IInventoryPanelView view;
        private IInventoryModel boundModel;
        private bool disposed;

        public IInventoryModel BoundModel => boundModel;

        public InventoryPanelController(IInventoryPanelView view)
        {
            this.view = view ?? throw new ArgumentNullException(nameof(view));
        }

        public void Bind(IInventoryModel model)
        {
            ThrowIfDisposed();

            if (ReferenceEquals(boundModel, model))
            {
                if (boundModel != null)
                    view.Render(boundModel);

                return;
            }

            Unsubscribe();

            boundModel = model;

            if (boundModel == null)
            {
                view.Clear();
                return;
            }

            boundModel.Changed += HandleModelChanged;
            view.Render(boundModel);
        }

        public void Unbind()
        {
            if (disposed)
                return;

            Unsubscribe();
            boundModel = null;
            view.Clear();
        }

        private void HandleModelChanged(InventoryChangedEvent change)
        {
            if (boundModel != null)
                view.Render(boundModel);
        }

        private void Unsubscribe()
        {
            if (boundModel != null)
                boundModel.Changed -= HandleModelChanged;
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(InventoryPanelController));
        }

        public void Dispose()
        {
            if (disposed)
                return;

            Unbind();
            disposed = true;
        }
    }
}

using UnityEngine;
using UnityEngine.UIElements;

namespace Systems.Inventory.Architecture
{
    /// <summary>
    /// Simple reusable tooltip section with an optional heading and body text.
    /// Serves as a ready-to-use example and a base for more specialized presentation sections.
    /// </summary>
    [CreateAssetMenu(fileName = "Text Tooltip Section", menuName = "Inventory/Tooltip Sections/Text")]
    public sealed class InventoryTextTooltipSection : InventoryItemTooltipSection
    {
        [SerializeField] private string heading;
        [SerializeField, TextArea(2, 6)] private string body;

        public string Heading => heading;
        public string Body => body;

        public override VisualElement CreateContent(InventoryItemTooltipContext context)
        {
            VisualElement container = new VisualElement();
            container.AddToClassList("inventoryItemTooltip-textSection");

            if (!string.IsNullOrWhiteSpace(heading))
            {
                Label title = new Label(heading);
                title.AddToClassList("inventoryItemTooltip-sectionHeading");
                title.style.color = Color.white;
                container.Add(title);
            }

            if (!string.IsNullOrWhiteSpace(body))
            {
                Label description = new Label(body);
                description.AddToClassList("inventoryItemTooltip-sectionBody");
                description.style.whiteSpace = WhiteSpace.Normal;
                description.style.color = Color.white;
                container.Add(description);
            }

            return container;
        }
    }
}

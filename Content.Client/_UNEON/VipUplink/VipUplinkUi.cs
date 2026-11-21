using Content.Client.UserInterface.Fragments;
using Content.Shared._UNEON.VipUplink;
using Content.Shared.CartridgeLoader;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client._UNEON.VipUplink;

public sealed partial class VipUplinkUi : UIFragment
{
    private sealed class VipUplinkPanel : BoxContainer;
    private static readonly Color Accent = Color.FromHex("#E8B4FF");
    private readonly Dictionary<VipUplinkCategory, BoxContainer> _pages = new();
    private readonly Dictionary<VipUplinkCategory, Button> _tabs = new();
    private readonly Dictionary<string, Button> _buttons = new();
    private VipUplinkPanel _root = default!;
    private Label _balance = default!;
    private BoundUserInterface _ui = default!;
    private VipUplinkCategory _selected;
    private string? _catalogue;

    public override Control GetUIFragmentRoot() => _root;

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _ui = userInterface;
        _buttons.Clear();
        _pages.Clear();
        _tabs.Clear();
        _catalogue = null;
        _root = new VipUplinkPanel { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 6, HorizontalExpand = true };
        var header = new PanelContainer { PanelOverride = new StyleBoxFlat(Color.FromHex("#372646")) };
        var heading = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, Margin = new Thickness(8) };
        heading.AddChild(new Label { Text = Loc.GetString("uneon-vip-title"), FontColorOverride = Accent });
        _balance = new Label { Text = Loc.GetString("uneon-vip-loading"), ClipText = true };
        heading.AddChild(_balance);
        header.AddChild(heading);
        _root.AddChild(header);

        var firstRow = new BoxContainer { SeparationOverride = 4 };
        var secondRow = new BoxContainer { SeparationOverride = 4 };
        _root.AddChild(firstRow);
        _root.AddChild(secondRow);

        AddTab(firstRow, VipUplinkCategory.Resources, "uneon-vip-tab-resources");
        AddTab(firstRow, VipUplinkCategory.Hardsuits, "uneon-vip-tab-hardsuits");
        AddTab(firstRow, VipUplinkCategory.Food, "uneon-vip-tab-food");
        AddTab(secondRow, VipUplinkCategory.Cloaks, "uneon-vip-tab-cloaks");
        AddTab(secondRow, VipUplinkCategory.Misc, "uneon-vip-tab-misc");

        foreach (var page in _pages.Values)
        {
            var scroll = new ScrollContainer { VerticalExpand = true, HorizontalExpand = true, MinHeight = 200 };
            scroll.AddChild(page);
            _root.AddChild(scroll);
        }
        SelectTab(VipUplinkCategory.Resources);
    }

    private void AddTab(BoxContainer row, VipUplinkCategory category, string label)
    {
        var button = new Button { Text = Loc.GetString(label), ToggleMode = true, HorizontalExpand = true };
        button.OnPressed += _ => SelectTab(category);
        row.AddChild(button);
        _tabs.Add(category, button);
        _pages.Add(category, new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 4
        });
    }

    private void SelectTab(VipUplinkCategory category)
    {
        _selected = category;
        foreach (var (key, page) in _pages)
        {
            _tabs[key].Pressed = key == _selected;
            if (page.Parent is ScrollContainer scroll)
                scroll.Visible = key == _selected;
        }
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is not VipUplinkState vip)
            return;
        _balance.Text = Loc.GetString(vip.Allowed ? "uneon-vip-balance" : "uneon-vip-access-inactive",
            ("balance", vip.Balance));

        var catalogue = string.Join("|", vip.Products.ConvertAll(p => $"{p.Id}:{p.Prototype}:{p.Price}:{p.Name}:{p.Category}:{p.StackAmount}"));
        if (_catalogue != catalogue)
        {
            _catalogue = catalogue;
            foreach (var page in _pages.Values)
                page.RemoveAllChildren();
            _buttons.Clear();

            var prototypes = IoCManager.Resolve<IPrototypeManager>();
            foreach (var product in vip.Products)
            {
                if (!_pages.TryGetValue(product.Category, out var page))
                    continue;

                var name = product.Name is { } key ? Loc.GetString(key) :
                    prototypes.TryIndex<EntityPrototype>(product.Prototype, out var entity) ? entity.Name : product.Prototype;
                var icon = new EntityPrototypeView { SetSize = new(32), Stretch = SpriteView.StretchMode.Fit };
                icon.SetPrototype(product.Prototype);
                var card = new PanelContainer { PanelOverride = new StyleBoxFlat(Color.FromHex("#282333")) };
                var row = new BoxContainer { SeparationOverride = 6, Margin = new Thickness(5) };
                row.AddChild(icon);
                var details = new BoxContainer
                {
                    Orientation = BoxContainer.LayoutOrientation.Vertical,
                    HorizontalExpand = true,
                    VerticalAlignment = Control.VAlignment.Center
                };
                details.AddChild(new Label { Text = name, ClipText = true, ToolTip = name });
                if (product.StackAmount > 0)
                    details.AddChild(new Label { Text = Loc.GetString("uneon-vip-quantity", ("count", product.StackAmount)), FontColorOverride = Accent });
                row.AddChild(details);

                var button = new Button
                {
                    Text = Loc.GetString("uneon-vip-price", ("price", product.Price)),
                    ToolTip = Loc.GetString("uneon-vip-buy", ("name", name), ("price", product.Price)),
                    VerticalAlignment = Control.VAlignment.Center
                };
                button.OnPressed += _ =>
                {
                    button.Disabled = true;
                    _ui.SendMessage(new CartridgeUiMessage(new VipUplinkBuyMessage(product.Id)));
                };
                _buttons.Add(product.Id, button);
                row.AddChild(button);
                card.AddChild(row);
                page.AddChild(card);
            }

            foreach (var page in _pages.Values)
            {
                if (page.ChildCount == 0)
                    page.AddChild(new Label { Text = Loc.GetString("uneon-vip-empty") });
            }
        }

        foreach (var product in vip.Products)
        {
            if (_buttons.TryGetValue(product.Id, out var button))
                button.Disabled = !vip.Allowed || vip.Balance < product.Price;
        }
    }
}

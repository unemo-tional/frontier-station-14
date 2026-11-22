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
    private static readonly Color SecondaryText = Color.FromHex("#C3B9D2");
    private readonly Dictionary<VipUplinkCategory, BoxContainer> _pages = new();
    private readonly Dictionary<VipUplinkCategory, Button> _tabs = new();
    private readonly Dictionary<string, Button> _buttons = new();
    private VipUplinkPanel _root = default!;
    private Label _balance = default!;
    private Label _subscription = default!;
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
        _root = new VipUplinkPanel
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
            HorizontalExpand = true,
            VerticalExpand = true
        };
        var header = new PanelContainer { PanelOverride = new StyleBoxFlat(Color.FromHex("#352345")) };
        var heading = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, Margin = new Thickness(8), SeparationOverride = 3 };
        heading.AddChild(new Label { Text = Loc.GetString("uneon-vip-title"), FontColorOverride = Accent });
        _subscription = new Label { Text = Loc.GetString("uneon-vip-loading"), ClipText = true };
        _balance = new Label { ClipText = true, FontColorOverride = SecondaryText };
        heading.AddChild(_subscription);
        heading.AddChild(_balance);
        header.AddChild(heading);
        _root.AddChild(header);

        var body = new BoxContainer { SeparationOverride = 6, VerticalExpand = true };
        var rail = new PanelContainer { PanelOverride = new StyleBoxFlat(Color.FromHex("#21192C")) };
        var tabs = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 4,
            Margin = new Thickness(4)
        };
        rail.AddChild(tabs);
        body.AddChild(rail);
        _root.AddChild(body);

        AddTab(tabs, VipUplinkCategory.Resources, "uneon-vip-tab-resources");
        AddTab(tabs, VipUplinkCategory.Hardsuits, "uneon-vip-tab-hardsuits");
        AddTab(tabs, VipUplinkCategory.Food, "uneon-vip-tab-food");
        AddTab(tabs, VipUplinkCategory.Cloaks, "uneon-vip-tab-cloaks");
        AddTab(tabs, VipUplinkCategory.Misc, "uneon-vip-tab-misc");
        AddTab(tabs, VipUplinkCategory.Personal, "uneon-vip-tab-personal");

        foreach (var page in _pages.Values)
        {
            var scroll = new ScrollContainer { VerticalExpand = true, HorizontalExpand = true, HScrollEnabled = false };
            scroll.AddChild(page);
            body.AddChild(scroll);
        }
        SelectTab(VipUplinkCategory.Resources);
    }

    private void AddTab(BoxContainer tabs, VipUplinkCategory category, string label)
    {
        var button = new Button { Text = Loc.GetString(label), ToggleMode = true, MinHeight = 27 };
        button.OnPressed += _ => SelectTab(category);
        tabs.AddChild(button);
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
        var tier = Loc.GetString(vip.Tier switch
        {
            1 => "uneon-sub-tier-adventurer",
            2 => "uneon-sub-tier-cosmopioneer",
            3 => "uneon-sub-tier-shuttle-captain",
            4 => "uneon-sub-tier-sector-baron",
            5 => "uneon-sub-tier-frontier-legend",
            _ => "uneon-sub-tier-unknown"
        });
        _subscription.Text = vip.Allowed
            ? Loc.GetString("uneon-vip-subscription", ("tier", tier))
            : Loc.GetString("uneon-vip-access-inactive");
        _balance.Text = Loc.GetString("uneon-vip-balance", ("balance", vip.Balance));

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

                prototypes.TryIndex<EntityPrototype>(product.Prototype, out var entity);
                var name = product.Name is { } key ? Loc.GetString(key) : entity?.Name ?? product.Prototype;
                var icon = new EntityPrototypeView
                {
                    SetSize = new(36),
                    Stretch = SpriteView.StretchMode.Fit,
                    VerticalAlignment = Control.VAlignment.Top
                };
                icon.SetPrototype(product.Prototype);
                var card = new PanelContainer { PanelOverride = new StyleBoxFlat(Color.FromHex("#2C2438")) };
                var row = new BoxContainer { SeparationOverride = 6, Margin = new Thickness(7) };
                row.AddChild(icon);
                var details = new BoxContainer
                {
                    Orientation = BoxContainer.LayoutOrientation.Vertical,
                    HorizontalExpand = true,
                    SeparationOverride = 3
                };
                var title = new RichTextLabel();
                title.SetMessage(name, defaultColor: Accent);
                details.AddChild(title);
                if (!string.IsNullOrWhiteSpace(entity?.Description))
                {
                    var description = new RichTextLabel();
                    description.SetMessage(entity.Description, defaultColor: SecondaryText);
                    details.AddChild(description);
                }
                if (product.StackAmount > 0)
                    details.AddChild(new Label { Text = Loc.GetString("uneon-vip-quantity", ("count", product.StackAmount)), FontColorOverride = Accent });
                row.AddChild(details);

                var actions = new BoxContainer { SeparationOverride = 4 };
                actions.AddChild(new Label
                {
                    Text = Loc.GetString("uneon-vip-price", ("price", product.Price)),
                    ClipText = true,
                    FontColorOverride = Accent,
                    HorizontalExpand = true
                });
                var button = new Button
                {
                    Text = Loc.GetString("uneon-vip-purchase"),
                    ToolTip = Loc.GetString("uneon-vip-buy", ("name", name), ("price", product.Price)),
                    VerticalAlignment = Control.VAlignment.Center
                };
                button.OnPressed += _ =>
                {
                    button.Disabled = true;
                    _ui.SendMessage(new CartridgeUiMessage(new VipUplinkBuyMessage(product.Id)));
                };
                _buttons.Add(product.Id, button);
                actions.AddChild(button);
                details.AddChild(actions);
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

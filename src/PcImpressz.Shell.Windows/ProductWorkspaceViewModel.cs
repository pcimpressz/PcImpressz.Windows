using System.Windows.Input;
using PcImpressz.Shell.Customization;

namespace PcImpressz.Shell.Windows;

internal sealed class ProductWorkspaceViewModel : Observable
{
    public EntityPageViewModel Products { get; }
    public OperationsPageViewModel Inventory { get; }
    private int _tab;
    public int SelectedTab { get => _tab; set { _tab = value; Changed(); } }
    public ICommand SelectedStockCommand { get; }
    public ICommand AllStockCommand { get; }
    public ProductWorkspaceViewModel(EntityPageViewModel products, OperationsPageViewModel inventory)
    {
        (Products, Inventory) = (products, inventory);
        SelectedStockCommand = new RelayCommand(() =>
        {
            if (Products.SelectedRow is not { } row) { Products.Notify("Először jelölj ki egy mentett terméket."); return; }
            if (Inventory.FocusProduct(row)) SelectedTab = 1; else Products.Notify(Inventory.Message);
        });
        AllStockCommand = new RelayCommand(() => { if (Inventory.FocusProduct(null)) SelectedTab = 1; else Products.Notify(Inventory.Message); });
    }
}

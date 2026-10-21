using System.Windows.Input;
using PcImpressz.Shell.Customization;

namespace PcImpressz.Shell.Windows;

internal sealed class ProductWorkspaceViewModel : Observable
{
    public EntityPageViewModel Products { get; }
    public OperationsPageViewModel Inventory { get; }
    private int _tab;
    public int SelectedTab { get => _tab; set { _tab = value; Changed();Changed(nameof(IsStockExpanded)); } }
    public bool IsStockExpanded {get=>SelectedTab==2;set{if(value)SelectedTab=2;else if(SelectedTab==2)SelectedTab=0;}}
    public ICommand NewProductCommand { get; }
    public ICommand EditProductCommand { get; }
    public ICommand SelectedStockCommand { get; }
    public ICommand AllStockCommand { get; }
    public ProductWorkspaceViewModel(EntityPageViewModel products, OperationsPageViewModel inventory)
    {
        (Products, Inventory) = (products, inventory);
        products.StockRequested+=()=>SelectedStockCommand?.Execute(null);
        products.EditorRequested += () => SelectedTab = 1;
        NewProductCommand = new RelayCommand(() => Products.NewCommand.Execute(null));
        EditProductCommand = new RelayCommand(Products.RequestEditSelected);
        SelectedStockCommand = new RelayCommand(() =>
        {
            if (Products.SelectedRow is not { } row) { Products.Notify("Először jelölj ki egy mentett terméket."); return; }
            if (Inventory.FocusProduct(row)) SelectedTab = 2; else Products.Notify(Inventory.Message);
        });
        AllStockCommand = new RelayCommand(() => { if (Inventory.FocusProduct(null)) SelectedTab = 2; else Products.Notify(Inventory.Message); });
    }
}

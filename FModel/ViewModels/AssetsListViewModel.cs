using System.ComponentModel;
using CUE4Parse.FileProvider.Objects;
using FModel.Framework;

namespace FModel.ViewModels;

public class AssetsListViewModel
{
    public RangeObservableCollection<GameFileViewModel> Assets { get; } = [];

    private ListCollectionView _assetsView;
    public ListCollectionView AssetsView
    {
        get
        {
            _assetsView ??= new ListCollectionView(Assets)
            {
                SortDescriptions = { new SortDescription("Asset.Path", ListSortDirection.Ascending) }
            };
            return _assetsView;
        }
    }

    public void Add(GameFile gameFile) => Assets.Add(new GameFileViewModel(gameFile));
}

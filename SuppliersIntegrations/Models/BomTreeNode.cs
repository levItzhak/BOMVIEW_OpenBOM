using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using BOMVIEW.OpenBOM.Models;

namespace BOMVIEW.Models
{
    public class BomTreeNode : INotifyPropertyChanged
    {
        private bool _isLoading;
        private bool _hasUnloadedChildren;
        private bool _isExpanded;
        private ObservableCollection<BomTreeNode> _children;
        private NodeType _treeNodeType;
        private bool _isVisible = true;
        public BomTreeNode Parent { get; set; }


        public string Id { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }
        public bool IsBom { get; set; }

        // Enhanced node types to include both BOMs and Catalogs
        public enum NodeType
        {
            Bom,
            Catalog,
            Item
        }

        public NodeType TreeNodeType
        {
            get => _treeNodeType;
            set
            {
                _treeNodeType = value;
                OnPropertyChanged();
            }
        }

        public bool IsVisible
        {
            get => _isVisible;
            set
            {
                _isVisible = value;
                OnPropertyChanged();
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                _isLoading = value;
                OnPropertyChanged();
            }
        }

        public bool HasUnloadedChildren
        {
            get => _hasUnloadedChildren;
            set
            {
                _hasUnloadedChildren = value;
                OnPropertyChanged();
            }
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                _isExpanded = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<BomTreeNode> Children
        {
            get => _children ??= new ObservableCollection<BomTreeNode>();
            set
            {
                _children = value;
                foreach (var child in _children)
                {
                    child.Parent = this;
                }
                OnPropertyChanged();
            }
        }

        // Additional properties for catalog items
        public string PartNumber { get; set; }
        public string Description { get; set; }
        public Dictionary<string, string> Properties { get; set; } = new Dictionary<string, string>();

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // Helper method to create a node from a catalog item
        public static BomTreeNode FromCatalogItem(OpenBomListItem item)
        {
            return new BomTreeNode
            {
                Id = item.Id,
                Name = item.Name,
                Type = "folder",
                TreeNodeType = NodeType.Catalog,
                HasUnloadedChildren = true,
                PartNumber = item.PartNumber
            };
        }

        // Helper method to create a node from a BOM item
        public static BomTreeNode FromBomItem(OpenBomListItem item)
        {
            return new BomTreeNode
            {
                Id = item.Id,
                Name = item.Name,
                Type = "folder",
                TreeNodeType = NodeType.Bom,
                HasUnloadedChildren = true
            };
        }
    }
}
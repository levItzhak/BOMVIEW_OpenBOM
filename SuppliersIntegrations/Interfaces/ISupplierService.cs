using System.Threading.Tasks;
using BOMVIEW.Models;

namespace BOMVIEW.Interfaces
{
    public interface ISupplierService
    {
        /// <summary>
        /// Gets the price and availability data for a specific part number
        /// </summary>
        Task<SupplierData> GetPriceAndAvailabilityAsync(string partNumber);

        /// <summary>
        /// Gets the supplier type (DigiKey or Mouser)
        /// </summary>
        SupplierType SupplierType { get; }

        /// <summary>
        /// Adds items to cart (implemented only by Mouser)
        /// </summary>
    }

    public enum SupplierType
    {
        DigiKey,
        Mouser,
        Farnell,
        Israel
    }


}
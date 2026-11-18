using Grid.Api.Common;
using Grid.Api.Data;
using Grid.Api.Features.Catalogue;
using Microsoft.EntityFrameworkCore;

namespace Grid.Api.Features.Marketplace;

public sealed record PackageLineRequest(Guid? PackageId, int? Quantity);

/// <summary>A priced booking: one service, the packages chosen, and what it all costs.</summary>
public sealed record PricedBooking(ServiceCategory Service, List<ServiceRequestLine> Lines, decimal Total, string Title, string Scope, int DurationMinutes);

/// <summary>Prices a booking from GRID's package list. The client sends packages and quantities, never amounts.</summary>
public static class ServicePricing
{
    public static async Task<PricedBooking> QuoteAsync(GridDbContext db, IReadOnlyList<PackageLineRequest>? lines, CancellationToken ct)
    {
        if (lines is null || lines.Count == 0)
            throw Errors.Validation([new FieldError("lines", "Choose what you'd like done.")]);
        if (lines.Count > 10)
            throw Errors.Validation([new FieldError("lines", "Choose up to 10 items.")]);
        if (lines.Any(l => l.PackageId is null))
            throw Errors.Validation([new FieldError("lines", "Every item needs a package.")]);
        if (lines.Select(l => l.PackageId).Distinct().Count() != lines.Count)
            throw Errors.Validation([new FieldError("lines", "List each package once and set its quantity.")]);

        var ids = lines.Select(l => l.PackageId!.Value).ToList();
        var packages = await db.ServicePackages.Where(p => ids.Contains(p.Id) && p.IsActive).ToDictionaryAsync(p => p.Id, ct);
        if (packages.Count != ids.Count)
            throw Errors.NotFound("catalogue.package_not_found", "One of those options is no longer offered.");

        var categoryIds = packages.Values.Select(p => p.ServiceCategoryId).Distinct().ToList();
        if (categoryIds.Count != 1)
            throw Errors.Validation([new FieldError("lines", "Book one service at a time.")]);
        var service = await db.ServiceCategories.SingleAsync(c => c.Id == categoryIds[0], ct);
        if (!service.IsActive || !service.IsLaunched)
            throw new ApiException(422, "services.not_launched", $"{service.Name} can't be booked yet.");

        var v = new Validator();
        var priced = new List<ServiceRequestLine>();
        for (var i = 0; i < lines.Count; i++)
        {
            var package = packages[lines[i].PackageId!.Value];
            var quantity = lines[i].Quantity ?? 1;
            if (quantity < 1 || quantity > package.MaxQuantity)
            {
                v.Add($"lines[{i}].quantity", package.MaxQuantity == 1
                    ? $"{package.Name} is booked once."
                    : $"Choose between 1 and {package.MaxQuantity}.");
                continue;
            }
            priced.Add(new ServiceRequestLine
            {
                ServicePackageId = package.Id,
                Name = package.Group == package.Name ? package.Name : $"{package.Group} · {package.Name}",
                UnitLabel = package.UnitLabel,
                UnitPrice = package.Price,
                Quantity = quantity,
                Total = package.Price * quantity,
                SortOrder = i,
            });
        }
        v.ThrowIfInvalid();

        // Alternatives of one job (home sizes) can't be combined.
        var groups = lines.Select(l => packages[l.PackageId!.Value])
            .Where(p => p.MaxQuantity == 1).GroupBy(p => p.Group).Where(g => g.Count() > 1).ToList();
        if (groups.Count > 0)
            throw Errors.Validation([new FieldError("lines", $"Choose one size of {groups[0].Key.ToLowerInvariant()}.")]);

        var title = priced.Count == 1 ? priced[0].Name : $"{service.Name} · {priced.Count} items";
        var scope = string.Join(", ", priced.Select(l => l.Quantity > 1 ? $"{l.Quantity} × {l.Name}" : l.Name));
        var minutes = lines.Sum(l => packages[l.PackageId!.Value].DurationMinutes * (l.Quantity ?? 1));
        return new PricedBooking(service, priced, priced.Sum(l => l.Total), title.Length > 100 ? title[..100] : title,
            scope.Length > 200 ? scope[..197] + "…" : scope, minutes);
    }
}

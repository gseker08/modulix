using System.Text.Json.Nodes;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Modulix.Core.Contracts.Records;
using Modulix.Core.Hosting;
using Modulix.Shared.Crm;

namespace Modulix.Modules.Crm.Endpoints;

/// <summary>
/// CRM uç noktaları: /api/crm/{companies|contacts|opportunities|activities}. Tipli istekleri kayıt boru
/// hattına çevirir; tenant, modül hakkı, izin, doğrulama, audit ve olay boru hattında uygulanır.
/// Modül tenant için kapalıysa veya izin yoksa 403; başka tenant'ın kaydı 404 döner.
/// Yeni bir sektör modülü aynı kalıpla tek satırda kaynak ekler (<see cref="ResourceEndpoints.MapResource{TDto,TSave}"/>).
/// </summary>
internal static class CrmEndpoints
{
    /// <summary>
    /// <paramref name="routes"/> host tarafından <c>/api/crm</c> grubu olarak, oturum ve modül kontrolüyle sarılmış gelir (ADR-0046).
    /// </summary>
    public static void MapEndpoints(IEndpointRouteBuilder routes)
    {
        var crm = routes.MapGroup(string.Empty)
            .WithTags("CRM");

        ResourceEndpoints.MapResource<CompanyDto, SaveCompanyRequest>(crm, "/api/crm", "companies", CrmModule.Entities.Company,
            CrmModule.Permissions.CompaniesRead, CrmModule.Permissions.CompaniesWrite);
        crm.MapGet("/companies/{id:guid}/summary", CompanySummaryEndpoint.GetAsync).RequirePermission(CrmModule.Permissions.CompaniesRead);
        crm.MapGet("/companies/{id:guid}/timeline", CompanyTimelineEndpoint.GetAsync).RequirePermission(CrmModule.Permissions.CompaniesRead);
        crm.MapGet("/companies/{id:guid}/timeline/summary", CompanyTimelineSummaryEndpoint.GetAsync).RequirePermission(CrmModule.Permissions.CompaniesRead);
        DuplicateEndpoints.Map(crm);
        MergeEndpoints.Map(crm);
        ResourceEndpoints.MapResource<ContactDto, SaveContactRequest>(crm, "/api/crm", "contacts", CrmModule.Entities.Contact,
            CrmModule.Permissions.ContactsRead, CrmModule.Permissions.ContactsWrite);
        crm.MapGet("/contacts/{id:guid}/timeline", ContactTimelineEndpoint.GetAsync).RequirePermission(CrmModule.Permissions.ContactsRead);
        crm.MapGet("/contacts/{id:guid}/timeline/summary", ContactTimelineEndpoint.SummaryAsync).RequirePermission(CrmModule.Permissions.ContactsRead);
        ResourceEndpoints.MapResource<CompanyContactDto, SaveCompanyContactRequest>(crm, "/api/crm", "company-contacts", CrmModule.Entities.CompanyContact,
            CrmModule.Permissions.ContactsRead, CrmModule.Permissions.ContactsWrite);
        ResourceEndpoints.MapResource<LocationDto, SaveLocationRequest>(crm, "/api/crm", "locations", CrmModule.Entities.Location,
            CrmModule.Permissions.CompaniesRead, CrmModule.Permissions.CompaniesWrite);
        ResourceEndpoints.MapResource<AddressDto, SaveAddressRequest>(crm, "/api/crm", "addresses", CrmModule.Entities.Address,
            CrmModule.Permissions.CompaniesRead, CrmModule.Permissions.CompaniesWrite);
        ResourceEndpoints.MapResource<ContactPointDto, SaveContactPointRequest>(crm, "/api/crm", "contact-points", CrmModule.Entities.ContactPoint,
            CrmModule.Permissions.CompaniesRead, CrmModule.Permissions.CompaniesWrite);
        ResourceEndpoints.MapResource<PersonContactPointDto, SavePersonContactPointRequest>(crm, "/api/crm", "person-contact-points", CrmModule.Entities.PersonContactPoint,
            CrmModule.Permissions.ContactsRead, CrmModule.Permissions.ContactsWrite);
        ResourceEndpoints.MapResource<CompanyRelationDto, SaveCompanyRelationRequest>(crm, "/api/crm", "company-relations", CrmModule.Entities.CompanyRelation,
            CrmModule.Permissions.CompaniesRead, CrmModule.Permissions.CompaniesWrite);
        ResourceEndpoints.MapResource<ProductCategoryDto, SaveProductCategoryRequest>(crm, "/api/crm", "product-categories", CrmModule.Entities.ProductCategory,
            CrmModule.Permissions.ProductsRead, CrmModule.Permissions.ProductsWrite);
        ResourceEndpoints.MapResource<MeasureUnitDto, SaveMeasureUnitRequest>(crm, "/api/crm", "units", CrmModule.Entities.MeasureUnit,
            CrmModule.Permissions.ProductsRead, CrmModule.Permissions.ProductsWrite);
        ResourceEndpoints.MapResource<ProductDto, SaveProductRequest>(crm, "/api/crm", "products", CrmModule.Entities.Product,
            CrmModule.Permissions.ProductsRead, CrmModule.Permissions.ProductsWrite);
        PricingEndpoints.Map(crm);
        OpportunityEndpoints.Map(crm);
        ResourceEndpoints.MapResource<ActivityDto, SaveActivityRequest>(crm, "/api/crm", "activities", CrmModule.Entities.Activity,
            CrmModule.Permissions.ActivitiesRead, CrmModule.Permissions.ActivitiesWrite);
        ActivityEndpoints.Map(crm);
        LeadEndpoints.Map(crm);
        LeadIntakeEndpoints.MapAdmin(crm);
        SalesPlanningEndpoints.Map(crm);
        QuoteEndpoints.Map(crm);
    }
}

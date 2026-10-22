using Microsoft.Extensions.DependencyInjection;
using RemsToRems.Application.Features.PatientLookup;

namespace RemsToRems.Application
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddApplication(this IServiceCollection services)
        {
            services.AddScoped<IPatientLookupService, PatientLookupService>();

            return services;
        }
    }
}

using Microsoft.AspNetCore.Mvc;
using RemsToRems.Application;
using RemsToRems.Infrastructure;
using RemsToRems.Api.Filters;
using RemsToRems.Api.Models;

namespace RemsToRems.Api
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.

            builder.Services
                .AddControllers(options =>
                {
                    options.Filters.Add<ApiExceptionFilter>();
                })
                .ConfigureApiBehaviorOptions(options =>
                {
                    // Body/JSON binding errors (e.g. an invalid DateOfBirth) use the same ApiResponse shape.
                    options.InvalidModelStateResponseFactory = context =>
                    {
                        var invalid = context.ModelState
                            .Where(entry => entry.Value?.Errors.Count > 0)
                            .ToList();

                        // A bad JSON value also flags the whole body ("The request field is required.");
                        // report only the field-level ($.field) errors when there are any.
                        var fieldErrors = invalid.Where(entry => entry.Key.StartsWith('$')).ToList();
                        var errors = (fieldErrors.Count > 0 ? fieldErrors : invalid)
                            .Select(entry => $"{entry.Key}: {entry.Value!.Errors[0].ErrorMessage}");

                        return new BadRequestObjectResult(
                            ApiResponse<object>.Error(string.Join(" ", errors)));
                    };
                });
            builder.Services.AddApplication();
            builder.Services.AddInfrastructure(builder.Configuration);

            // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
            builder.Services.AddOpenApi();

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.MapOpenApi();
            }

            app.UseHttpsRedirection();

            app.UseAuthorization();

            app.MapControllers();

            app.Run();
        }
    }
}

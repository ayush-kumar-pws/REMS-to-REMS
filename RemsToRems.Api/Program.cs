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
                    // The match body is bound as a JsonElement, so binding fails only for an empty body or
                    // text that is not JSON. Parser messages are not returned; they name .NET types and offsets.
                    options.InvalidModelStateResponseFactory = context =>
                        ProblemResponses.MalformedBody(context.HttpContext);
                });
            // Shared by controller catch blocks and ApiExceptionFilter for unexpected (500) errors.
            builder.Services.AddScoped<RemsToRems.Api.Filters.UnexpectedErrorLogger>();
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

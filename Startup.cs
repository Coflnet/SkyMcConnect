using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Coflnet.Sky.McConnect.Models;
using Coflnet.Sky.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi;
using Prometheus;

namespace Coflnet.Sky.McConnect
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {

            services.AddHostedService<McConnectService>();
            services.AddControllers().AddNewtonsoftJson(c=>{
                c.SerializerSettings.DateTimeZoneHandling = Newtonsoft.Json.DateTimeZoneHandling.Utc;
            });
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "SkyMcConnect", Version = "v1" });
                var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
                var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
                c.IncludeXmlComments(xmlPath);
            });

            var serverVersion = new MariaDbServerVersion(new Version(Configuration["MARIADB_VERSION"]));
            AddDatabaseTls(services);
            var connectionString = DatabaseTls.ConnectionString(Configuration, "DB_CONNECTION");
            services.AddDbContext<ConnectContext>(
                (provider, dbContextOptions) =>
                {
                    dbContextOptions
                        .UseMySql(connectionString, serverVersion)
                        .EnableSensitiveDataLogging(); // <-- optional but helps with debugging
                    if (DatabaseTls.IsEnabled(Configuration))
                        dbContextOptions.AddInterceptors(provider.GetRequiredService<DatabaseTlsInterceptor>());
                }
            );
            services.AddSingleton<ConnectService>();
            services.AddSingleton<Kafka.KafkaCreator>();
            services.AddJaeger(Configuration);

        }

        private void AddDatabaseTls(IServiceCollection services)
        {
            if (!DatabaseTls.IsEnabled(Configuration))
                return;
            services.AddSingleton(_ => Coflnet.Core.Security.PemTlsOptions.FromConfiguration(Configuration.GetSection("DB:TLS")));
            services.AddSingleton<DatabaseTlsInterceptor>();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            app.UseExceptionHandler(errorApp =>
            {
                ErrorHandler.Add(app.ApplicationServices.GetRequiredService<ILogger<Startup>>(), errorApp, "mc-connect");
            });
            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "SkyMcConnect v1");
                c.RoutePrefix = "api";
            });


            app.UseHttpsRedirection();

            app.UseRouting();

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapMetrics();
                endpoints.MapControllers();
            });
        }
    }
}

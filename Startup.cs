using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using stream.Controllers;

namespace stream
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
            services.AddControllers();

            StreamControllerConfig streamControllerConfig = new StreamControllerConfig();
            Configuration.Bind("StreamControllerConfig", streamControllerConfig);
            services.AddSingleton(streamControllerConfig);

            StreamConfig streamConfig = new StreamConfig();
            Configuration.Bind("StreamConfig", streamConfig);
            services.AddSingleton(streamConfig);

            //Do something more with this later
            services.AddSingleton(new RandomNameAssociationConfig());

            //Only ONE system for every request!
            var provider = services.BuildServiceProvider();

            var system = ActivatorUtilities.CreateInstance<StreamSystem>(provider);
            var readonlynames = ActivatorUtilities.CreateInstance<RandomNameAssociation<string>>(provider);
            services.AddSingleton(system);
            services.AddSingleton(readonlynames);
            services.AddSingleton<IHostedService>(system); //AddHostedService<StreamSystem>();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseCors(x => x.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
            app.UseHttpsRedirection();
            app.UseRouting();
            app.UseAuthorization();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }
}

using DevChatter.Bot.Core;
using DevChatter.Bot.Core.Automation;
using DevChatter.Bot.Core.Caching;
using DevChatter.Bot.Core.Data;
using DevChatter.Bot.Core.Events;
using DevChatter.Bot.Core.GoogleApi;
using DevChatter.Bot.Core.Systems.Streaming;
using DevChatter.Bot.Core.Util;
using DevChatter.Bot.Infra.Ef;
using DevChatter.Bot.Infra.GoogleApi;
using DevChatter.Bot.Infra.Twitch;
using DevChatter.Bot.Infra.Web.Hubs;
using DevChatter.Bot.Web.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using DevChatter.Bot.Core.Commands;
using DevChatter.Bot.Core.Commands.Trackers;
using DevChatter.Bot.Core.Systems.Chat;
using DevChatter.Bot.Infra.Web;
using IHostingEnvironment = Microsoft.AspNetCore.Hosting.IHostingEnvironment;

namespace DevChatter.Bot.Web
{
    public class Startup
    {
        public Startup(IConfiguration configuration, IHostingEnvironment env)
        {
            Configuration = configuration;
        }

        public IContainer ApplicationContainer { get; private set; }


        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public IServiceProvider ConfigureServices(IServiceCollection services)
        {
            services.Configure<CookiePolicyOptions>(options =>
            {
                // This lambda determines whether user consent for non-essential cookies is needed for a given request.
                options.CheckConsentNeeded = context => true;
                options.MinimumSameSitePolicy = SameSiteMode.None;
            });

            services.Configure<CommandHandlerSettings>(Configuration.GetSection("CommandHandlerSettings"));
            services.Configure<TwitchClientSettings>(Configuration.GetSection("TwitchClientSettings"));
            services.Configure<GoogleCloudSettings>(Configuration.GetSection("GoogleCloudSettings"));

            services.AddDbContext<AppDataContext>(ServiceLifetime.Transient);

            services.AddMvc().SetCompatibilityVersion(CompatibilityVersion.Version_2_1);

            services.AddSignalR();

            var builder = new ContainerBuilder();
            builder.Populate(services);

            // register types here
            var fullConfig = Configuration.Get<BotConfiguration>();

            builder.RegisterInstance(fullConfig.TwitchClientSettings)
                .As<TwitchClientSettings>().SingleInstance();

            builder.RegisterInstance(fullConfig.CommandHandlerSettings)
                .AsSelf().SingleInstance();

            builder.RegisterInstance(fullConfig.GoogleCloudSettings)
                .AsSelf().SingleInstance();


            IRepository repository = SetUpDatabase.SetUpRepository(fullConfig.DatabaseConnectionString);

            builder.RegisterInstance(repository);

            RegisterTimezoneLookupClasses(builder);

            builder.RegisterAssemblyTypes(
                Assembly.GetAssembly(typeof(IRepository))
                , Assembly.GetAssembly(typeof(EfGenericRepo))
                , Assembly.GetAssembly(typeof(GoogleApiTimezoneLookup))
                , Assembly.GetAssembly(typeof(TwitchChatClient))
                , Assembly.GetAssembly(typeof(BotHub))
                , Assembly.GetAssembly(typeof(Program))
            );


            builder.RegisterGeneric(typeof(Logger<>))
                .As(typeof(ILogger<>)).SingleInstance();

            builder.RegisterGeneric(typeof(LoggerAdapter<>))
                .As(typeof(ILoggerAdapter<>)).SingleInstance();

            builder.RegisterGeneric(typeof(List<>))
                .As(typeof(IList<>)).SingleInstance();

            builder.RegisterGeneric(typeof(Lazier<>))
                .As(typeof(Lazy<>)).InstancePerRequest();

            builder.RegisterType<AutomationSystem>()
                .As<IAutomatedActionSystem>().SingleInstance();
            builder.RegisterType<CommandHandler>()
                .As<ICommandHandler>().SingleInstance();

            builder.RegisterType<ChatUserCollection>()
                .As<IChatUserCollection>()
                .SingleInstance();

            builder.RegisterType<SettingsFactory>()
                .As<ISettingsFactory>();

            builder.RegisterType<CommandCooldownTracker>()
                .As<ICommandUsageTracker>();

            builder.RegisterType<SystemClock>()
                .As<IClock>();


            //services.AddSingleton<IStreamingPlatform, StreamingPlatform>();
            //services.AddSingleton<IClock, SystemClock>();

            //services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
            //services.AddSingleton(typeof(ILoggerAdapter<>), typeof(LoggerAdapter<>));
            //services.AddSingleton<ISettingsFactory, SettingsFactory>();

            //services.AddSingleton<IChatUserCollection, ChatUserCollection>();

            //services.AddSingleton(typeof(IList<>), typeof(List<>));
            //services.AddTransient(typeof(Lazy<>), typeof(Lazier<>));

            //services.AddSingleton<IOverlayNotification, BotHubOverlayNotification>();
            
            //services.AddAllGames();

            //services.AddStreamMetaCommands();

            //services.AddCurrencySystem();

            //services.AddSimpleCommandsFromRepository(repository);

            //services.AddCommandSystem();
            builder.Register(p =>
                new CommandList(p.Resolve<IList<IBotCommand>>().ToList(), p));


            builder.AddTwitchLibConnection(fullConfig.TwitchClientSettings);

            builder.RegisterType<AutomationSystem>()
                .As<IAutomatedActionSystem>().SingleInstance();

            builder.RegisterType<BotMain>().AsSelf().SingleInstance();

            builder.RegisterType<DevChatterBotBackgroundWorker>()
                .As<IHostedService>();

            builder.RegisterType<CurrencyGenerator>()
                .As<ICurrencyGenerator>()
                .SingleInstance();

            ApplicationContainer = builder.Build();

            return new AutofacServiceProvider(ApplicationContainer);
        }

        private static void RegisterTimezoneLookupClasses(ContainerBuilder builder)
        {
            builder.RegisterType<GoogleApiTimezoneLookup>().AsImplementedInterfaces();
            builder.RegisterType<EfCacheLayer>().AsImplementedInterfaces();
            //builder.RegisterType<ITimezoneLookup>(provider =>
            //    new CachedTimezoneLookup(provider.GetService<GoogleApiTimezoneLookup>(), provider.GetService<ICacheLayer>()));
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Error");
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseCookiePolicy();

            app.UseSignalR(routes =>
            {
                routes.MapHub<BotHub>("/BotHub");
            });

            app.UseMvc();
        }
    }

    internal class Lazier<T> : Lazy<T> where T : class
    {
        public Lazier(IServiceProvider provider)
            : base(provider.GetRequiredService<T>)
        {
        }
    }
}

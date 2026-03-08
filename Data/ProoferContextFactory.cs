using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Proofer.Data
{
    public class ProoferContextFactory : IDesignTimeDbContextFactory<ProoferContext>
    {
        public ProoferContext CreateDbContext(string[] args)
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json")
                .Build();

            var optionsBuilder = new DbContextOptionsBuilder<ProoferContext>();
            optionsBuilder.UseSqlServer(config.GetConnectionString("ProoferDb"));

            return new ProoferContext(optionsBuilder.Options);


        }
    }
}

using System;
using System.Collections.Generic;
using System.Configuration;
using System.Runtime.Serialization.Formatters;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Proofer.Models;
using static Proofer.Enums;

namespace Proofer.Data
{
    public class ProoferContext : DbContext
    {
        public DbSet<Person> People { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<Form> Forms { get; set; }
        public DbSet<Note> Notes { get; set; }

        public ProoferContext(DbContextOptions<ProoferContext> options) : base(options)
        { 
        }
    }
}

using Proofer.Models;
using System;
using System.Collections.Generic;
using System.Security.Permissions;
using System.Text;
using static Proofer.Enums;

namespace Proofer
{
    public class Person
    {

        //simple properties
        public int Id { get; private set; }
        public string? FirstName { get;  private set; }
        public string? LastName { get; private set; } 
        public DateTime Birthdate { get;  private set; }
        public DateTime EffectiveDate { get;  private set; }
        public string? Bio { get;  private set; }
        public WaiverType Waiver { get;  private set; } = WaiverType.None;
        public string FullName => $"{FirstName} {LastName}".Trim();

        //collections
        public List<Form> Forms { get; private set; } = new List<Form>();
        public List<Note> Notes { get; private set; } = new List<Note>();

        //constructor
        protected Person() { }

        //methods
        public static Person CreatePerson(string firstName, string lastName, string Bio, DateTime birthdate, DateTime effective, WaiverType waiver) 
        {
            var person = new Person
            {
                FirstName = firstName.Trim(),
                LastName = lastName.Trim(),
                Bio = Bio.Trim(),
                Birthdate = birthdate,
                EffectiveDate = effective,
                Waiver = waiver
            };

                var forms = GenerateFormList(person.EffectiveDate);
                person.Forms = forms;
            
            return person;
        }

        public static List<Form> GenerateFormList(DateTime effective)
        {
            var list = new List<Form>();
            list.Add(new Form { DueDate = effective.AddDays(90), Type = FormType.Q1R });
            list.Add(new Form { DueDate = effective.AddDays(180), Type = FormType.Q2R });
            list.Add(new Form { DueDate = effective.AddDays(270), Type = FormType.Q3R });
            list.Add(new Form { DueDate = effective.AddDays(365), Type = FormType.Q4R });
            list.Add(new Form { DueDate = effective.AddDays(365), Type = FormType.PCP });
            list.Add(new Form { DueDate = effective.AddDays(270), Type = FormType.Reclassification });
            list.Add(new Form { DueDate = effective.AddDays(270), Type = FormType.ComprehensiveAssessment });
            list.Add(new Form { DueDate = effective.AddDays(365), Type = FormType.Release_Agency });
            list.Add(new Form { DueDate = effective.AddDays(365), Type = FormType.Release_DHHS });
            list.Add(new Form { DueDate = effective.AddDays(365), Type = FormType.Release_Medical });

            return list;
        }
    }
}

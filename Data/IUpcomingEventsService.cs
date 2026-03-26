using Sati.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Sati.Data
{
    public interface IUpcomingEventService
    {
        List<UpcomingEvent> GenerateEvents(IEnumerable<Person> people, Settings settings);
    }
}

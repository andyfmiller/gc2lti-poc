﻿using System.ComponentModel.DataAnnotations;
using gc2lti_shared.Data;

namespace gc2lti_shared.Models
{
    /// <summary>
    /// Key/value pair used by <see cref="EfDataStore"/>
    /// </summary>
    public class Item
    {
        [Key]
        [MaxLength(100)]
        public string Key { get; set; }

        [MaxLength(500)]
        public string Value { get; set; }
    }
}

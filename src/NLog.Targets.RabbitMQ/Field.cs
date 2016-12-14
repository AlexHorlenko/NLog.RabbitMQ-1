﻿using NLog.Config;
using NLog.Layouts;

namespace NLog.Targets
{
	[NLogConfigurationItem]
	[ThreadAgnostic]
	public class Field
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="Field" /> class.
		/// </summary>
		public Field() : this(null, null, null)
		{
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="Field" /> class.
		/// </summary>
		/// <param name="name">The field display name</param>
		/// <param name="layout">The field layout</param>
		public Field(string key, string name, Layout layout)
		{
			this.Key = key;
		    this.Name = string.IsNullOrEmpty(name) ? key : name;
		    this.Layout = layout;
		}

        /// <summary>
        /// Gets or sets the name of the field
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the key of the field
        /// </summary>
        [RequiredParameter]
        public string Key { get; set; }

		/// <summary>
		/// Gets or sets the layout of the field
		/// </summary>
		[RequiredParameter]
		public Layout Layout { get; set; }
	}
}
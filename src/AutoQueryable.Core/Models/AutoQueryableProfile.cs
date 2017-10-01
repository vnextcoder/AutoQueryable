﻿using System;
using AutoQueryable.Core.Enums;

namespace AutoQueryable.Core.Models
{
    public class AutoQueryableProfile
    {
        public string[] SelectableProperties { get; set; }
        
        public string[] UnselectableProperties { get; set; }

        public string[] SortableProperties { get; set; }
        
        public string[] UnSortableProperties { get; set; }

        public string[] GroupableProperties { get; set; }
        
        public string[] UnGroupableProperties { get; set; }

        public ClauseType? AllowedClauses { get; set; }
        
        public ClauseType? DisAllowedClauses { get; set; }

        public ConditionType? AllowedConditions { get; set; }
        
        public ConditionType? DisAllowedConditions { get; set; }

        public WrapperPartType? AllowedWrapperPartType { get; set; }
        
        public WrapperPartType? DisAllowedWrapperPartType { get; set; }

        public int? MaxToTake { get; set; }

        public int? MaxToSkip { get; set; }
        
        public int? MaxDepth { get; set; }

        public Type ClauseProviderType { get; set; }
    }
}

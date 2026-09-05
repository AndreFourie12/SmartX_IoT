using System;
using System.Collections.Generic;
using System.Text;

namespace SmartX.Shared.Models
{
    /// <summary>
    /// The 3 fields contained in the JSON payload.
    /// DataType tells the API which of three types to trust and read based on the sensor
    /// The other two of the three will arrive as null 
    /// </summary>
   
    public enum TelemetryDataType
    {
        Float,
        Int,
        Bool
    }
}

//------------------------------------------- End Of File -------------------------------------------//

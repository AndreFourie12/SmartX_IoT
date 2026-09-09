using System;
using System.Collections.Generic;
using System.Text;

namespace SmartX.Shared.Validation
{
    /// <summary>
    /// 
    /// </summary>
    /// <param name="IsValid"></param>
    /// <param name="Errors"></param>
    public sealed record DeploymentValidatorResult(bool IsValid, IReadOnlyList<string> Errors);

}

//------------------------------------------- End Of File -------------------------------------------//
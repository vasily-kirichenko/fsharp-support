﻿using JetBrains.ReSharper.Psi;

namespace JetBrains.ReSharper.Plugins.FSharp.Psi
{
  public interface IFSharpTypeMember : ITypeMember
  {
    bool IsVisibleFromFSharp { get; }
  }
}
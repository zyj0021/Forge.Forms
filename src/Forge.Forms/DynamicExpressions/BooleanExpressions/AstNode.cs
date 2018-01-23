﻿using System;

namespace Forge.Forms.DynamicExpressions.BooleanExpressions
{
    internal abstract class AstNode
    {
        public abstract bool Evaluate(bool[] values);

        public static AstNode Parse(string expression)
        {
            throw new NotImplementedException();
        }
    }

    internal class AndOperator : AstNode
    {
        public AstNode Left { get; set; }

        public AstNode Right { get; set; }

        public override bool Evaluate(bool[] values)
        {
            return Left.Evaluate(values) && Right.Evaluate(values);
        }
    }

    internal class OrOperator : AstNode
    {
        public AstNode Left { get; set; }

        public AstNode Right { get; set; }

        public override bool Evaluate(bool[] values)
        {
            return Left.Evaluate(values) || Right.Evaluate(values);
        }
    }

    internal class NotOperator : AstNode
    {
        public AstNode Child { get; set; }

        public override bool Evaluate(bool[] values)
        {
            return !Child.Evaluate(values);
        }
    }

    internal class ValueNode : AstNode
    {
        public int Index { get; set; }

        public override bool Evaluate(bool[] values)
        {
            return values[Index];
        }
    }
}

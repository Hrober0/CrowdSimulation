using System;
using FluentAssertions;
using FluentAssertions.Execution;
using Unity.Mathematics;
using UnityEngine;

namespace Tests.TestsUtilities
{
    public class VectorAssertion<T> where T : struct, IEquatable<T>
    {
        public VectorAssertion(T[] vector)
        {
            Vector = vector;
        }

        public T[] Vector { get; }

        public AndConstraint<T[]> Be(T expected1, string because = "", params object[] becauseArgs)
        {
            Execute.Assertion
                   .ForCondition(Vector.Length == 1 && Vector[0].Equals(expected1))
                   .BecauseOf(because, becauseArgs)
                   .FailWith("Expected {context:value} to be ({0}){reason}, but found {1}", expected1, Vector);

            return new AndConstraint<T[]>(Vector);
        }
        
        public AndConstraint<T[]> Be(T expected1, T expected2, string because = "", params object[] becauseArgs)
        {
            Execute.Assertion
                   .ForCondition(Vector.Length == 2 && Vector[0].Equals(expected1) && Vector[1].Equals(expected2))
                   .BecauseOf(because, becauseArgs)
                   .FailWith("Expected {context:value} to be ({0}, {1}){reason}, but found {2}", expected1, expected2, Vector);

            return new AndConstraint<T[]>(Vector);
        }
        
        public AndConstraint<T[]> Be(T expected1, T expected2, T expected3, string because = "", params object[] becauseArgs)
        {
            Execute.Assertion
                   .ForCondition(Vector.Length == 3 && Vector[0].Equals(expected1) && Vector[1].Equals(expected2) && Vector[2].Equals(expected3))
                   .BecauseOf(because, becauseArgs)
                   .FailWith("Expected {context:value} to be ({0}, {1}, {2}){reason}, but found {3}", expected1, expected2, expected3, Vector);

            return new AndConstraint<T[]>(Vector);
        }
    }
    
    
    public static class Float2AssertionExtensions
    {
        public static VectorAssertion<float> Should(this float2 value)
        {
            return new VectorAssertion<float>(new[] { value.x, value.y });
        }
        
        public static AndConstraint<float2> BeApproximately(this VectorAssertion<float> assertion, float2 expected, string because = "", params object[] becauseArgs)
        {
            Execute.Assertion
                   .ForCondition(assertion.Vector.Length == 2
                                 && Mathf.Approximately(assertion.Vector[0], expected.x)
                                 && Mathf.Approximately(assertion.Vector[1], expected.y))
                   .BecauseOf(because, becauseArgs)
                   .FailWith("Expected {context:value} to be {0}{reason}, but found {1}", expected, assertion.Vector);

            return new AndConstraint<float2>(new(assertion.Vector[0], assertion.Vector[1]));
        }
    }
}
// SPDX-FileCopyrightText: 2025 Ethereum Classic Community
// SPDX-License-Identifier: Apache-2.0

using System.Numerics;
using FluentAssertions;
using NUnit.Framework;

namespace Nethermind.Consensus.Classic.Test;

[TestFixture]
[Parallelizable(ParallelScope.All)]
public class DifficultyBombCalculatorTests
{
    private const long DieHard = 3_000_000;
    private const long Gotham = 5_000_000;
    private const long Ecip1041 = 5_900_000;

    [Test]
    public void TimeBomb_PreActivation_Is_Zero() => DifficultyBombCalculator.CalculateTimeBomb(100_000, DieHard, Gotham, Ecip1041)
            .Should().Be(BigInteger.Zero);

    [Test]
    public void TimeBomb_Grows_Exponentially()
    {
        DifficultyBombCalculator.CalculateTimeBomb(300_000, DieHard, Gotham, Ecip1041).Should().Be(BigInteger.Pow(2, 1));
        DifficultyBombCalculator.CalculateTimeBomb(400_000, DieHard, Gotham, Ecip1041).Should().Be(BigInteger.Pow(2, 2));
    }

    [Test]
    public void DieHard_Pauses_Bomb()
    {
        BigInteger expected = BigInteger.Pow(2, 28);
        DifficultyBombCalculator.CalculateTimeBomb(3_000_000, DieHard, Gotham, Ecip1041).Should().Be(expected);
        DifficultyBombCalculator.CalculateTimeBomb(4_000_000, DieHard, Gotham, Ecip1041).Should().Be(expected, "bomb stays paused");
    }

    [Test]
    public void Gotham_Resumes_Bomb() => DifficultyBombCalculator.CalculateTimeBomb(5_000_000, DieHard, Gotham, Ecip1041)
            .Should().BeGreaterThan(BigInteger.Zero);

    [Test]
    public void Ecip1041_Removes_Bomb()
    {
        DifficultyBombCalculator.CalculateTimeBomb(5_899_999, DieHard, Gotham, Ecip1041).Should().BeGreaterThan(BigInteger.Zero);
        DifficultyBombCalculator.CalculateTimeBomb(5_900_000, DieHard, Gotham, Ecip1041).Should().Be(BigInteger.Zero);
        DifficultyBombCalculator.CalculateTimeBomb(100_000_000, DieHard, Gotham, Ecip1041).Should().Be(BigInteger.Zero);
    }

    [Test]
    public void Mordor_Has_No_Bomb() => DifficultyBombCalculator.CalculateTimeBomb(50_000_000, null, null, null)
            .Should().Be(BigInteger.Zero);
}

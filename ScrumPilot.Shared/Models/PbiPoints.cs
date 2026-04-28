namespace ScrumPilot.Shared.Models
{
    /// <summary>
    /// Fibonacci-based story point values used during planning poker estimation.
    /// </summary>
    public enum PbiPoints
    {
        /// <summary>0 points — item requires no meaningful effort or is unpointed.</summary>
        Zero = 0,
        /// <summary>1 point — trivial change.</summary>
        One = 1,
        /// <summary>2 points — small, well-understood task.</summary>
        Two = 2,
        /// <summary>3 points — moderate task with some unknowns.</summary>
        Three = 3,
        /// <summary>5 points — larger task with meaningful complexity.</summary>
        Five = 5,
        /// <summary>8 points — complex item; consider breaking down.</summary>
        Eight = 8,
        /// <summary>13 points — very complex; strong candidate for splitting.</summary>
        Thirteen = 13,
        /// <summary>21 points — epic-sized; should be split before committing.</summary>
        TwentyOne = 21
    }
}

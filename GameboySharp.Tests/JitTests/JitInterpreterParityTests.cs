using Xunit;

namespace GameboySharp.Tests.JitTests;

public class JitInterpreterParityTests
{
    public static IEnumerable<object[]> InstructionSequences => new List<object[]>
    {
        // instrCount must include the terminating JP instruction to match the JIT block
        new object[] { "LD_reg_imm", new byte[] { 0x06, 0x42, 0x0E, 0x13, 0x16, 0x24, 0xC3, 0x00, 0x00 }, 4 },
        new object[] { "LD_reg_reg", new byte[] { 0x06, 0x42, 0x48, 0x51, 0xC3, 0x00, 0x00 }, 4 },
        new object[] { "ADD_A_r", new byte[] { 0x3E, 0x10, 0x06, 0x20, 0x80, 0xC3, 0x00, 0x00 }, 4 },
        new object[] { "SUB_A_r", new byte[] { 0x3E, 0x30, 0x06, 0x10, 0x90, 0xC3, 0x00, 0x00 }, 4 },
        new object[] { "INC_DEC", new byte[] { 0x3E, 0xFF, 0x3C, 0x3D, 0x3D, 0xC3, 0x00, 0x00 }, 5 },
        new object[] { "AND_OR_XOR", new byte[] { 0x3E, 0xF0, 0x06, 0x0F, 0xA0, 0xB0, 0xA8, 0xC3, 0x00, 0x00 }, 6 },
        new object[] { "CP_flags", new byte[] { 0x3E, 0x42, 0xFE, 0x42, 0xC3, 0x00, 0x00 }, 3 },
        new object[] { "RLCA_RRCA", new byte[] { 0x3E, 0x85, 0x07, 0x0F, 0xC3, 0x00, 0x00 }, 4 },
        new object[] { "RLA_RRA", new byte[] { 0x3E, 0x85, 0x17, 0x1F, 0xC3, 0x00, 0x00 }, 4 },
        new object[] { "DAA", new byte[] { 0x3E, 0x15, 0x06, 0x27, 0x80, 0x27, 0xC3, 0x00, 0x00 }, 5 },
        new object[] { "CPL_SCF_CCF", new byte[] { 0x3E, 0xAA, 0x2F, 0x37, 0x3F, 0xC3, 0x00, 0x00 }, 5 },
        new object[] { "LD16_BC", new byte[] { 0x01, 0x34, 0x12, 0xC3, 0x00, 0x00 }, 2 },
        new object[] { "LD16_DE", new byte[] { 0x11, 0x78, 0x56, 0xC3, 0x00, 0x00 }, 2 },
        new object[] { "LD16_HL", new byte[] { 0x21, 0xBC, 0x9A, 0xC3, 0x00, 0x00 }, 2 },
        new object[] { "INC16_DEC16", new byte[] { 0x01, 0xFF, 0x00, 0x03, 0x0B, 0xC3, 0x00, 0x00 }, 4 },
        new object[] { "ADD_HL_BC", new byte[] { 0x21, 0x00, 0x10, 0x01, 0x00, 0x01, 0x09, 0xC3, 0x00, 0x00 }, 4 },
        new object[] { "NOP_sequence", new byte[] { 0x00, 0x00, 0x00, 0xC3, 0x00, 0x00 }, 4 },
        new object[] { "ADC_A_n", new byte[] { 0x3E, 0xFF, 0x37, 0xCE, 0x01, 0xC3, 0x00, 0x00 }, 4 },
        new object[] { "SBC_A_n", new byte[] { 0x3E, 0x10, 0x37, 0xDE, 0x05, 0xC3, 0x00, 0x00 }, 4 },
        new object[] { "XOR_A", new byte[] { 0x3E, 0xFF, 0xAF, 0xC3, 0x00, 0x00 }, 3 },
    };

    [Theory]
    [MemberData(nameof(InstructionSequences))]
    public void JitMatchesInterpreter(string name, byte[] code, int instrCount)
    {
        // Run through interpreter
        var interpHelper = new JitTestHelper();
        interpHelper.LoadCode(0x0000, code);
        var interpSnap = interpHelper.RunInterpreter(instrCount);

        // Run through JIT (executes as a single block)
        var jitHelper = new JitTestHelper();
        jitHelper.LoadCode(0x0000, code);
        // JIT executes the whole block in one Step() call
        var jitSnap = jitHelper.RunJit(1);

        Assert.Equal(interpSnap.A, jitSnap.A);
        Assert.Equal(interpSnap.F, jitSnap.F);
        Assert.Equal(interpSnap.B, jitSnap.B);
        Assert.Equal(interpSnap.C, jitSnap.C);
        Assert.Equal(interpSnap.D, jitSnap.D);
        Assert.Equal(interpSnap.E, jitSnap.E);
        Assert.Equal(interpSnap.H, jitSnap.H);
        Assert.Equal(interpSnap.L, jitSnap.L);
        Assert.Equal(interpSnap.SP, jitSnap.SP);
        Assert.Equal(interpSnap.PC, jitSnap.PC);
    }
}

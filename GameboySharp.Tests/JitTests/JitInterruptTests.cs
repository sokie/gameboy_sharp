using GameboySharp.Jit;
using Xunit;

namespace GameboySharp.Tests.JitTests;

public class JitInterruptTests
{
    [Fact]
    public void JitBlock_ExitsEarly_WhenInterruptPending()
    {
        var helper = new JitTestHelper();
        helper.LoadCode(0x0000, 0x00, 0x00, 0x00, 0x00, 0xC3, 0x00, 0x00);
        // 4 NOPs + JP 0x0000

        // Set IME and pending VBlank interrupt BEFORE running
        helper.Cpu.InterruptMasterEnable = true;
        helper.Cpu.ie_register = 0x01; // VBlank enabled
        helper.Cpu.if_register = 0x01; // VBlank pending

        helper.JitCpu.Step();

        // Block should have exited early after the first instruction
        // and the interrupt handler should have set PC to the VBlank vector (0x0040)
        Assert.Equal(0x0040, helper.Cpu.PC);
    }

    [Fact]
    public void JitBlock_EI_EndsBlock()
    {
        var helper = new JitTestHelper();
        // NOP / EI (block should end at EI)
        helper.LoadCode(0x0000, 0x00, 0xFB);
        var block = BlockDecoder.Decode(0x0000, 0, helper.Mmu);

        Assert.Equal(2, block.InstructionCount);
        Assert.Equal(InstructionKind.EnableInterrupts, block.Instructions[1].Kind);
    }

    [Fact]
    public void JitBlock_NoInterrupt_RunsFullBlock()
    {
        var helper = new JitTestHelper();
        // 3 NOPs + JP 0x0000, no interrupts enabled
        helper.LoadCode(0x0000, 0x00, 0x00, 0x00, 0xC3, 0x00, 0x00);
        helper.Cpu.InterruptMasterEnable = false;

        helper.JitCpu.Step();

        // Block should complete fully, PC at jump target (0x0000)
        Assert.Equal(0x0000, helper.Cpu.PC);
    }

    [Fact]
    public void JitBlock_HaltFallsBackToInterpreter()
    {
        var helper = new JitTestHelper();
        helper.LoadCode(0x0000, 0x00, 0xC3, 0x00, 0x00);
        helper.Cpu.IsHaltBugActive = true;

        // Should fall back to interpreter which handles halt bug
        helper.JitCpu.Step();

        // With halt bug active, PC should NOT be incremented
        Assert.Equal(0x0000, helper.Cpu.PC);
    }

    [Fact]
    public void JitBlock_EIDelay_FallsBackToInterpreter()
    {
        var helper = new JitTestHelper();
        helper.LoadCode(0x0000, 0x00, 0xC3, 0x00, 0x00);
        // Simulate EI delay (1 instruction remaining)
        helper.Cpu._enableInterruptsScheduled = 1;

        helper.JitCpu.Step();

        // After one instruction with delay, IME should be enabled
        Assert.True(helper.Cpu.InterruptMasterEnable);
    }
}

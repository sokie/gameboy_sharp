using System.Linq.Expressions;
using System.Reflection;

namespace GameboySharp.Jit
{
    internal static class BlockCompiler
    {
        private static readonly ParameterExpression CpuParam = Expression.Parameter(typeof(Cpu), "cpu");
        private static readonly ParameterExpression MmuParam = Expression.Parameter(typeof(Mmu), "mmu");

        // CPU field/property accessors
        private static readonly MemberExpression RegPC = Expression.Field(CpuParam, nameof(Cpu.PC));
        private static readonly MemberExpression RegA = Expression.Field(CpuParam, nameof(Cpu.A));
        private static readonly MemberExpression RegF = Expression.Field(CpuParam, nameof(Cpu.F));

        // Interrupt state
        private static readonly MemberExpression IME = Expression.Field(CpuParam, nameof(Cpu._interruptMasterEnable));
        private static readonly MemberExpression IERegister = Expression.Property(CpuParam, nameof(Cpu.ie_register));
        private static readonly MemberExpression IFRegister = Expression.Property(CpuParam, nameof(Cpu.if_register));

        // MMU methods
        private static readonly MethodInfo ReadByteMethod = typeof(Mmu).GetMethod(nameof(Mmu.ReadByte))!;

        // Sync method on JitCpu - will be bound per-compilation via closure
        private static readonly MethodInfo SyncMethod = typeof(JitCpu).GetMethod(nameof(JitCpu.SyncSubsystems))!;

        public static void Compile(BasicBlock block, JitCpu jitCpu)
        {
            var bodyExpressions = new List<Expression>();
            var cyclesVar = Expression.Variable(typeof(int), "cycles");
            var returnTarget = Expression.Label(typeof(int), "returnLabel");

            // int cycles = 0;
            bodyExpressions.Add(Expression.Assign(cyclesVar, Expression.Constant(0)));

            for (int i = 0; i < block.InstructionCount; i++)
            {
                ref readonly var instr = ref block.Instructions[i];
                bool isLast = (i == block.InstructionCount - 1);

                // Emit instruction execution
                EmitInstruction(bodyExpressions, cyclesVar, ref block.Instructions[i], jitCpu, isLast);

                // Emit sync call: jitCpu.SyncSubsystems(instrCycles, mmu)
                // We sync with the cycles for this individual instruction
                EmitSync(bodyExpressions, ref block.Instructions[i], jitCpu);

                // Emit interrupt check (early exit if interrupt pending)
                // Skip on the last instruction - we'll return normally
                if (!isLast)
                {
                    EmitInterruptCheck(bodyExpressions, cyclesVar, ref block.Instructions[i], returnTarget);
                }
            }

            // Return total cycles
            bodyExpressions.Add(Expression.Label(returnTarget, cyclesVar));

            var body = Expression.Block(
                new[] { cyclesVar },
                bodyExpressions
            );

            var lambda = Expression.Lambda<Func<Cpu, Mmu, int>>(body, CpuParam, MmuParam);
            block.CompiledExecute = lambda.Compile();
        }

        private static void EmitInstruction(
            List<Expression> body,
            ParameterExpression cyclesVar,
            ref DecodedInstruction instr,
            JitCpu jitCpu,
            bool isLast)
        {
            // Set PC to this instruction's address (needed by delegate fallback)
            body.Add(Expression.Assign(RegPC, Expression.Constant(instr.Address)));

            // Call the original opcode delegate from the lookup table
            Expression cyclesExpr;

            if (instr.IsCBPrefixed)
            {
                // For CB-prefixed: set PC so the delegate reads operands correctly
                // The delegate expects PC to point at the CB prefix byte
                var delegateCall = Expression.Invoke(
                    Expression.Constant(Cpu.ExtendedOpcodeTable[instr.Opcode].Execute),
                    CpuParam, MmuParam);
                cyclesExpr = delegateCall;
            }
            else
            {
                var delegateCall = Expression.Invoke(
                    Expression.Constant(Cpu.OpcodeTable[instr.Opcode].Execute),
                    CpuParam, MmuParam);
                cyclesExpr = delegateCall;
            }

            // cycles += delegate(cpu, mmu);
            body.Add(Expression.AddAssign(cyclesVar, cyclesExpr));

            // Handle PC advancement for non-jump instructions
            // The delegate already handles PC for jumps (AutoIncrementPC = false)
            // For delegate fallback, we need to advance PC if AutoIncrementPC is true
            bool autoIncrement;
            if (instr.IsCBPrefixed)
            {
                autoIncrement = Cpu.ExtendedOpcodeTable[instr.Opcode].AutoIncrementPC;
            }
            else
            {
                autoIncrement = Cpu.OpcodeTable[instr.Opcode].AutoIncrementPC;
            }

            if (autoIncrement)
            {
                body.Add(Expression.Assign(RegPC,
                    Expression.Convert(
                        Expression.Add(
                            Expression.Convert(RegPC, typeof(int)),
                            Expression.Constant(instr.Bytes)),
                        typeof(ushort))));
            }
        }

        private static void EmitSync(
            List<Expression> body,
            ref DecodedInstruction instr,
            JitCpu jitCpu)
        {
            // jitCpu.SyncSubsystems(cycles, mmu)
            // For delegate fallback, we use the base cycle count
            // (conditional branches return different counts, but the delegate handles that)
            var syncCall = Expression.Call(
                Expression.Constant(jitCpu),
                SyncMethod,
                Expression.Constant(instr.Cycles),
                MmuParam);
            body.Add(syncCall);
        }

        private static void EmitInterruptCheck(
            List<Expression> body,
            ParameterExpression cyclesVar,
            ref DecodedInstruction instr,
            LabelTarget returnTarget)
        {
            // if (cpu._interruptMasterEnable && (cpu.ie_register & cpu.if_register & 0x1F) != 0)
            //     return cycles;
            var interruptPending = Expression.AndAlso(
                IME,
                Expression.NotEqual(
                    Expression.And(
                        Expression.And(
                            Expression.Convert(IERegister, typeof(int)),
                            Expression.Convert(IFRegister, typeof(int))),
                        Expression.Constant(0x1F)),
                    Expression.Constant(0)));

            body.Add(Expression.IfThen(
                interruptPending,
                Expression.Return(returnTarget, cyclesVar)));
        }
    }
}

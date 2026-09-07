#ifndef __COMPILE_H__
#define __COMPILE_H__

struct vertex_program_exec
{
	u32 data[4];
};

struct vertex_program_data
{
	s32 index;
	f32 value[4];
};

class CParser;

class CCompilerVP
{
public:
	CCompilerVP();
	virtual ~CCompilerVP();

	void Compile(CParser *pParser);

	int GetInputMask() const {return m_nInputMask;}
	int GetOutputMask() const {return m_nOutputMask;}
	int GetNumRegs() const { return m_nNumRegs; }
	int GetInstructionCount() const { return m_nInstructions; }
	struct vertex_program_exec* GetInstructions() const { return m_pInstructions;}
	std::list<struct nvfx_relocation> GetConstRelocations() const { return m_lConstRelocation; }
	std::list<struct nvfx_relocation> GetBranchRelocations() const { return m_lBranchRelocation; }

private:
	void Prepare(CParser *pParser);

	void emit_insn(struct nvfx_insn *insn,u8 opcode);
	void emit_dst(struct nvfx_insn *insn,u8 slot);
	void emit_src(struct nvfx_insn *insn,u8 pos);
	void emit_pow(struct nvfx_insn *insn);
	void emit_abs(struct nvfx_insn *insn);
	void emit_sub(struct nvfx_insn *insn);
	void emit_tex(struct nvfx_insn *insn);
	void emit_lrp(struct nvfx_insn *insn);
	void emit_nop();

	int grow_insns(int count);

	struct nvfx_reg temp();
	struct nvfx_reg constant(s32 pipe,f32 x,f32 y,f32 z,f32 w);

	void release_temps();

	inline param GetInputAttrib(int index)
	{
		s32 i;
		std::list<param>::iterator it = m_lParameters.begin();
		for(;it!=m_lParameters.end();it++) {
			for(i=0;i<it->count;i++) {
				if((int)(it->index + i)==index) {
					if(!it->is_const && !it->is_internal)
						return *it;
				}
			}
		}
		return param();
	}

	int m_nNumRegs;
	int m_nInputMask;
	int m_nOutputMask;

	int m_nInstructions;
	int m_nCurInstruction;
	struct vertex_program_exec *m_pInstructions;

	int m_nConsts;
	struct vertex_program_data *m_pConstData;

	u64 m_rTemps;
	u64 m_rTempsDiscard;

	struct nvfx_reg *m_rConst;

	std::list<param> m_lParameters;
	std::list<struct nvfx_relocation> m_lConstRelocation;
	std::list<struct nvfx_relocation> m_lBranchRelocation;
};

#endif

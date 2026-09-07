#ifndef __FPPARSER_H__
#define __FPPARSER_H__

#include "parser.h"

#define NV_OPTION_FP1		0x01
#define NV_OPTION_FP2		0x02
#define NV_OPTION_FP3		0x04

typedef struct _oparam
{
	std::string alias;
	s32 index;
	u8 is_fp16;
} oparam;

class CFPParser : public CParser
{
public:
	CFPParser();
	virtual ~CFPParser();

	virtual int Parse(const char *str);

private:
	void ParseInstruction(struct nvfx_insn *insn,opcode *opc,const char *param_str);
	void ParseMaskedDstReg(const char *token,struct nvfx_insn *insn);
	void ParseVectorSrc(const char *token,struct nvfx_src *reg);
	void ParseScalarSrc(const char *token,struct nvfx_src *reg);

	void ParseOutput(const char *param_str);

	const char* ParseOutputReg(const char *token,s32 *reg,u8 *is_fp16);
	const char* ParseInputReg(const char *token,s32 *reg);
	const char* ParseOutputRegAlias(const char *token,s32 *reg,u8 *is_fp16);

	void SetNoneDestReg(struct nvfx_insn *insn);
	
	u8 IsPCDisablingInstruction(struct nvfx_insn *insn);

	opcode FindOpcode(const char *mnemonic);

	int GetConstRegType(int index);

	virtual s32 ConvertInputReg(const char *token);
	virtual const char* ParseOutputMask(const char *token,u8 *mask);

	std::list<oparam> m_lOParameters;
};

#endif

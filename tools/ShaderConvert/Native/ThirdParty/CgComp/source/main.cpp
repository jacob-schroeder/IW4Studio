#include "types.h"
#include "fpparser.h"
#include "vpparser.h"
#include "compilervp.h"
#include "compilerfp.h"
#include <cstdlib>
#include <cstring>
#include <exception>
#include <memory>
#include <new>
#include <stdexcept>
#include <stdint.h>

#ifdef __BIG_ENDIAN__
#define SWAP16(v) (v)
#define SWAP32(v) (v)
#else
#define SWAP16(v) ((v)>>8)|((v)<<8)
#define SWAP32(v) ((v)>>24)|((v)<<24)|(((v)&0xFF00)<<8)|(((v)&0xFF0000)>>8)
#endif

static u32 endian_fp(u32 v)
{
  return ( ( ( v >> 16 ) & 0xffff ) << 0 ) |
         ( ( ( v >> 0 ) & 0xffff ) << 16 );
}

static const size_t program_capacity = 2 * 1024 * 1024;

static void require_space(size_t offset, size_t bytes)
{
	if (offset > program_capacity || bytes > program_capacity - offset)
		throw std::runtime_error("RSX program exceeds the supported binary size.");
}

static void set_error(char **error, const char *message) noexcept
{
	if (error == NULL) return;
	size_t size = strlen(message) + 1;
	*error = (char*)malloc(size);
	if (*error != NULL) memcpy(*error, message, size);
}

static void compileVP(const char *prg, void **output, size_t *length)
{
	CVPParser parser;
	CCompilerVP compiler;

	parser.Parse(prg);
	compiler.Compile(&parser);

	struct vertex_program_exec *vpi = compiler.GetInstructions();
	std::list<struct nvfx_relocation> branch_reloc = compiler.GetBranchRelocations();
	for(std::list<struct nvfx_relocation>::iterator it = branch_reloc.begin();it!=branch_reloc.end();it++) {
		struct vertex_program_exec *vpe = &vpi[it->location];

		vpe->data[3] &= ~NV40_VP_INST_IADDRL_MASK;
		vpe->data[3] |= (it->target&7) << NV40_VP_INST_IADDRL_SHIFT;

		vpe->data[2] &= ~NV40_VP_INST_IADDRH_MASK;
		vpe->data[2] |= ((it->target >> 3)&0x3f) << NV40_VP_INST_IADDRH_SHIFT;
	}

	std::list<struct nvfx_relocation> const_reloc = compiler.GetConstRelocations();
	for(std::list<struct nvfx_relocation>::iterator it = const_reloc.begin();it!=const_reloc.end();it++) {
		struct vertex_program_exec *vpe = &vpi[it->location];
		vpe->data[1] &= ~NVFX_VP(INST_CONST_SRC_MASK);
		vpe->data[1] |= (it->target) << NVFX_VP(INST_CONST_SRC_SHIFT);
	}

	int n,i;
	u16 magic = ('V'<<8)|'P';
	u32 lastoff = sizeof(rsxVertexProgram);
	std::unique_ptr<unsigned char, decltype(&free)> allocation(
		(unsigned char*)calloc(program_capacity, 1), free);
	unsigned char *vertexprogram = allocation.get();
	if (vertexprogram == NULL) throw std::bad_alloc();

	rsxVertexProgram *vp = (rsxVertexProgram*)vertexprogram;

	vp->magic = SWAP16(magic);
	vp->insn_start = SWAP16(0);
	vp->const_start = SWAP16(0);
	vp->num_regs = SWAP16(compiler.GetNumRegs());
	vp->input_mask = SWAP32(compiler.GetInputMask());
	vp->output_mask = SWAP32(compiler.GetOutputMask());

	lastoff = (lastoff + 3) & ~3u;

	rsxProgramAttrib *attribs = (rsxProgramAttrib*)(vertexprogram + lastoff);

	vp->attr_off = SWAP32(lastoff);

	n = 0;
	std::list<param> params = parser.GetParameters();
	for(std::list<param>::iterator it = params.begin();it!=params.end();it++) {
		if(!it->is_const) {
			it->user = lastoff + (n*sizeof(rsxProgramAttrib));
			require_space(lastoff, (n + 1) * sizeof(*attribs));
			attribs[n].index = SWAP32(it->index);
			attribs[n].name_off = SWAP32(0);
			attribs[n].type = it->type;
			n++;
		}
	}
	vp->num_attr = SWAP16(n);
	lastoff += (n*sizeof(rsxProgramAttrib));

	lastoff = (lastoff + 3) & ~3u;

	rsxProgramConst *consts = (rsxProgramConst*)(vertexprogram + lastoff);

	vp->const_off = SWAP32(lastoff);

	n = 0;
	for(std::list<param>::iterator it = params.begin();it!=params.end();it++) {
		if(it->is_const) {
			it->user = lastoff + (n*sizeof(rsxProgramConst));
			if (n + it->count > UINT16_MAX) throw std::runtime_error("Too many RSX constant records.");
			require_space(lastoff, (n + it->count) * sizeof(*consts));
			consts[n].count = it->count;
			consts[n].type = it->type;
			consts[n].is_internal = it->is_internal;
			consts[n].name_off = SWAP32(0);
			for(i=0;i<(s32)it->count;i++) {
				f32 *pVal = it->values[i];
				consts[n].index = SWAP32(it->index + i);
				consts[n].values[0].f = pVal[0];
				consts[n].values[0].u = SWAP32(consts[n].values[0].u);
				consts[n].values[1].f = pVal[1];
				consts[n].values[1].u = SWAP32(consts[n].values[1].u);
				consts[n].values[2].f = pVal[2];
				consts[n].values[2].u = SWAP32(consts[n].values[2].u);
				consts[n].values[3].f = pVal[3];
				consts[n].values[3].u = SWAP32(consts[n].values[3].u);
				n++;
			}
		}
	}
	vp->num_const = SWAP16(n);
	lastoff += (n*sizeof(rsxProgramConst));

	lastoff = (lastoff + 3) & ~3u;

	for(std::list<param>::iterator it = params.begin();it!=params.end();it++) {
		if(!it->name.empty() && !it->is_internal) {
			const char *name = it->name.c_str();
			int off = lastoff;
			require_space(lastoff, it->name.size() + 1);
			while(*name)
				vertexprogram[lastoff++] = *name++;
			vertexprogram[lastoff++] = 0;
			*((u32*)(vertexprogram + it->user)) = SWAP32(off);
		}
	}

	lastoff = (lastoff + 15) & ~15u;

	vp->ucode_off = SWAP32(lastoff);
	vp->num_insn = SWAP16(compiler.GetInstructionCount());

	require_space(lastoff, (size_t)compiler.GetInstructionCount() * 16);
	u32 *dstcodeptr = (u32*)(vertexprogram + lastoff);
	for(i=0,n=0;i<compiler.GetInstructionCount();i++,n+=4,lastoff+=16) {
		dstcodeptr[n+0] = SWAP32(vpi[i].data[0]);
		dstcodeptr[n+1] = SWAP32(vpi[i].data[1]);
		dstcodeptr[n+2] = SWAP32(vpi[i].data[2]);
		dstcodeptr[n+3] = SWAP32(vpi[i].data[3]);
	}

	*output = allocation.release();
	*length = lastoff;
}

static void compileFP(const char *prg, void **output, size_t *length)
{
	CFPParser parser;
	CCompilerFP compiler;

	parser.Parse(prg);
	compiler.Compile(&parser);

	int n,i;
	u16 magic = ('F'<<8)|'P';
	u32 lastoff = sizeof(rsxFragmentProgram);
	std::unique_ptr<unsigned char, decltype(&free)> allocation(
		(unsigned char*)calloc(program_capacity, 1), free);
	unsigned char *fragmentprogram = allocation.get();
	if (fragmentprogram == NULL) throw std::bad_alloc();

	rsxFragmentProgram *fp = (rsxFragmentProgram*)fragmentprogram;

	fp->magic = SWAP16(magic);
	fp->num_regs = SWAP16(compiler.GetNumRegs());
	fp->fp_control = SWAP32(compiler.GetFPControl());
	fp->texcoords = SWAP16(compiler.GetTexcoords());
	fp->texcoord2D = SWAP16(compiler.GetTexcoord2D());
	fp->texcoord3D = SWAP16(compiler.GetTexcoord3D());
	fp->input_mask = SWAP32(compiler.GetInputMask());

	std::list<struct fragment_program_data> const_relocs = compiler.GetConstRelocations();
	fp->reloc_count = SWAP32((u32)const_relocs.size());

	lastoff = (lastoff + 3) & ~3u;

	fp->reloc_off = SWAP32(lastoff);
	require_space(lastoff, const_relocs.size() * sizeof(rsxFragmentProgramRelocation));
	rsxFragmentProgramRelocation *relocs = (rsxFragmentProgramRelocation*)(fragmentprogram + lastoff);
	n = 0;
	for(std::list<struct fragment_program_data>::iterator d=const_relocs.begin();d!=const_relocs.end();d++) {
		relocs[n].index = SWAP32((u32)d->index);
		relocs[n].offset = SWAP32(d->offset*16);
		n++;
	}
	lastoff += n*sizeof(rsxFragmentProgramRelocation);

	lastoff = (lastoff + 3) & ~3u;

	fp->attr_off = SWAP32(lastoff);
	rsxProgramAttrib *attribs = (rsxProgramAttrib*)(fragmentprogram + lastoff);

	n = 0;
	std::list<param> params = parser.GetParameters();
	for(std::list<param>::iterator it = params.begin();it!=params.end();it++) {
		if(!it->is_const) {
			it->user = lastoff + (n*sizeof(rsxProgramAttrib));
			require_space(lastoff, (n + 1) * sizeof(*attribs));
			attribs[n].index = SWAP32(it->index);
			attribs[n].name_off = SWAP32(0);
			attribs[n].type = it->type;
			n++;
		}
	}
	fp->num_attr = SWAP16(n);
	lastoff += (n*sizeof(rsxProgramAttrib));

	lastoff = (lastoff + 3) & ~3u;

	for(std::list<param>::iterator it = params.begin();it!=params.end();it++) {
		const auto& param = *it;
		if(param.is_const && !param.is_internal) {
			for(i=0;i<param.count;i++) {
				s32 k = 0;
				require_space(lastoff, sizeof(u32));
				rsxConstOffsetTable *const_table = (rsxConstOffsetTable*)(fragmentprogram + lastoff);

				const_table->num = SWAP32(0);
				for(std::list<struct fragment_program_data>::iterator d=const_relocs.begin();d!=const_relocs.end();d++) {
					auto& reloc = *d;
					if(reloc.index==(param.index + i)) {
						u32 offs = (reloc.offset*16);
						require_space(lastoff, (size_t)(k + 2) * sizeof(u32));
						const_table->offset[k++] = SWAP32(offs);
						reloc.user = lastoff;
					}
				}
				const_table->num = SWAP32(k);
				lastoff += (4*k + 4);
			}
		}
	}

	lastoff = (lastoff + 3) & ~3u;

	fp->const_off = SWAP32(lastoff);
	rsxProgramConst *consts = (rsxProgramConst*)(fragmentprogram + lastoff);

	n = 0;
	for(std::list<param>::iterator it = params.begin();it!=params.end();it++) {
		if(it->is_const && !it->is_internal) {
			it->user = lastoff + (n*sizeof(rsxProgramConst));

			if (n + it->count > UINT16_MAX) throw std::runtime_error("Too many RSX constant records.");
			require_space(lastoff, (n + it->count) * sizeof(*consts));
			consts[n].count = it->count;
			consts[n].type = it->type;
			consts[n].is_internal = it->is_internal;
			consts[n].name_off = SWAP32(0);

			for(i=0;i<it->count;i++) {
				s32 table_off = -1;
				for(std::list<struct fragment_program_data>::iterator d=const_relocs.begin();d!=const_relocs.end();d++) {
					if(d->index==(it->index + i)) {
						table_off = d->user;
						break;
					}
				}

				f32 *pVal = it->values[i];
				consts[n].index = SWAP32(table_off);
				consts[n].values[0].f = pVal[0];
				consts[n].values[0].u = SWAP32(consts[n].values[0].u);
				consts[n].values[1].f = pVal[1];
				consts[n].values[1].u = SWAP32(consts[n].values[1].u);
				consts[n].values[2].f = pVal[2];
				consts[n].values[2].u = SWAP32(consts[n].values[2].u);
				consts[n].values[3].f = pVal[3];
				consts[n].values[3].u = SWAP32(consts[n].values[3].u);
				n++;
			}
		}
	}
	fp->num_const = SWAP16(n);
	lastoff += (n*sizeof(rsxProgramConst));

	lastoff = (lastoff + 3) & ~3u;

	for(std::list<param>::iterator it = params.begin();it!=params.end();it++) {
		if(!it->name.empty() && !it->is_internal) {
			const char *name = it->name.c_str();
			int off = lastoff;
			require_space(lastoff, it->name.size() + 1);
			while(*name)
				fragmentprogram[lastoff++] = *name++;
			fragmentprogram[lastoff++] = 0;
			*((u32*)(fragmentprogram + it->user)) = SWAP32(off);
		}
	}

	lastoff = (lastoff + 15) & ~15u;

	fp->ucode_off = SWAP32(lastoff);
	fp->num_insn = SWAP16(compiler.GetInstructionCount());

	struct fragment_program_exec *fpi = compiler.GetInstructions();
	require_space(lastoff, (size_t)compiler.GetInstructionCount() * 16);
	u32 *dstcodeptr = (u32*)(fragmentprogram + lastoff);
	for(i=0,n=0;i<compiler.GetInstructionCount();i++,n+=4,lastoff+=16) {
		dstcodeptr[n+0] = endian_fp((SWAP32(fpi[i].data[0])));
		dstcodeptr[n+1] = endian_fp((SWAP32(fpi[i].data[1])));
		dstcodeptr[n+2] = endian_fp((SWAP32(fpi[i].data[2])));
		dstcodeptr[n+3] = endian_fp((SWAP32(fpi[i].data[3])));
	}

	*output = allocation.release();
	*length = lastoff;
}

#if defined(_WIN32)
#define IW4_CGCOMP_EXPORT __declspec(dllexport)
#else
#define IW4_CGCOMP_EXPORT __attribute__((visibility("default")))
#endif

extern "C" IW4_CGCOMP_EXPORT int iw4_assemble_rsx(
	const char *assembly, int pixel, void **output, size_t *length, char **error) noexcept
{
	if (output != NULL) *output = NULL;
	if (length != NULL) *length = 0;
	if (error != NULL) *error = NULL;
	try {
		if (assembly == NULL || output == NULL || length == NULL || error == NULL ||
			(pixel != 0 && pixel != 1))
			throw std::invalid_argument("Invalid RSX assembler argument.");
		if (pixel) compileFP(assembly, output, length);
		else compileVP(assembly, output, length);
		return 0;
	} catch (const std::exception &exception) {
		set_error(error, exception.what());
	} catch (...) {
		set_error(error, "Unknown RSX assembler failure.");
	}
	return 1;
}

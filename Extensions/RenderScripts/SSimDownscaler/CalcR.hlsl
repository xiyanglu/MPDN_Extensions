// -- Misc --
sampler sMean:	register(s1);
sampler sH:	register(s2);

#define AverageFormat float2x4

#define Kernel(x) pow(0.25, abs(x))
#define taps 3	
#define maxtaps taps
#define Correction (1 - (1 + 4*pow(Kernel(1), 2) + 4 * pow(Kernel(2),2)) / pow(1 + 4*Kernel(1) + 4*Kernel(2), 2))

// -- Define horizontal convolver --
#define EntryPoint ScaleH
#define sqr(x)	((x)*(x))
#define Get(pos) float2x4(sqr(GetFrom(s0, pos) - mean), GetFrom(sH, pos))
#define axis 0

#define OutputFormat  float2x4
#define ExtraArguments float4 mean

#include "./Scalers/Convolver.hlsl"

#undef ExtraArguments
#undef OutputFormat

// -- Define vertical convolver --
#define EntryPoint 			main
#define Initialization		float4 mean = tex2D(sMean, tex)
#define Get(pos) 			ScaleH(pos, mean)
#define PostProcessing(S)	(S[0] == 0) ? 0 : sqrt(1 + S[1] * Correction / S[0])
// #define PostProcessing(S)	(S[0] == 0) ? 0 : sqrt(1 + S[1] / S[0])
#define axis 1
#include "./Scalers/Convolver.hlsl"
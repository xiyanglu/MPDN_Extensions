// This file is a part of MPDN Extensions.
// https://github.com/zachsaw/MPDN_Extensions
//
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 3.0 of the License, or (at your option) any later version.
// 
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public
// License along with this library.
// 
// -- Misc --
sampler s0 : register(s0);
sampler s1 : register(s1);
float4 p0 :  register(c0);
float2 p1 :  register(c1);

#define width  (p0[0])
#define height (p0[1])

#define dxdy (p1.xy)

#include "../Common/ColourProcessing.hlsl"

// -- Main code --
float4 main(float2 tex : TEXCOORD0) : COLOR {
    float4 c0 = tex2D(s0, tex);
    float4 c1 = tex2D(s1, tex);

	c0.xyz = Gamma(c0.rgb);
    float3 diff = c0.xyz - c1.xyz;

    return float4(diff, Luma(c0));
}

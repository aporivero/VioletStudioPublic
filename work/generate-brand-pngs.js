const fs = require('node:fs');
const path = require('node:path');
const zlib = require('node:zlib');

const size = 128;
function canvas() { return { width:size, height:size, data:Buffer.alloc(size*size*4) }; }
function pixel(c,x,y,color) { x=Math.round(x);y=Math.round(y);if(x<0||y<0||x>=size||y>=size)return;const i=(y*size+x)*4;c.data[i]=color[0];c.data[i+1]=color[1];c.data[i+2]=color[2];c.data[i+3]=color[3]??255; }
function rounded(c,x,y,w,h,r,color,condition=true) { const minX=Math.max(0,Math.floor(x)),maxX=Math.min(size-1,Math.ceil(x+w)),minY=Math.max(0,Math.floor(y)),maxY=Math.min(size-1,Math.ceil(y+h));for(let py=minY;py<=maxY;py++)for(let px=minX;px<=maxX;px++){const qx=Math.max(x+r,Math.min(px,x+w-r)),qy=Math.max(y+r,Math.min(py,y+h-r));if((px-qx)**2+(py-qy)**2>r*r)continue;const choice=typeof condition==='function'?condition(px,py):true;if(choice===false)continue;pixel(c,px,py,Array.isArray(choice)?choice:color);} }
function circle(c,cx,cy,r,color,inner=0) { const x0=Math.max(0,Math.floor(cx-r)),x1=Math.min(size-1,Math.ceil(cx+r)),y0=Math.max(0,Math.floor(cy-r)),y1=Math.min(size-1,Math.ceil(cy+r));for(let y=y0;y<=y1;y++)for(let x=x0;x<=x1;x++){const d=(x-cx)**2+(y-cy)**2;if(d<=r*r&&d>=inner*inner)pixel(c,x,y,color);} }
function line(c,x1,y1,x2,y2,width,color) { const r=width/2,dx=x2-x1,dy=y2-y1,n=Math.max(1,Math.ceil(Math.hypot(dx,dy)*2));for(let i=0;i<=n;i++){const t=i/n;circle(c,x1+dx*t,y1+dy*t,r,color);} }
function polygon(c,pts,color) { const ys=pts.map(p=>p[1]),xs=pts.map(p=>p[0]);for(let y=Math.max(0,Math.floor(Math.min(...ys)));y<=Math.min(size-1,Math.ceil(Math.max(...ys)));y++)for(let x=Math.max(0,Math.floor(Math.min(...xs)));x<=Math.min(size-1,Math.ceil(Math.max(...xs)));x++){let inside=false;for(let i=0,j=pts.length-1;i<pts.length;j=i++){const [xi,yi]=pts[i],[xj,yj]=pts[j];if(((yi>y)!==(yj>y))&&(x<(xj-xi)*(y-yi)/(yj-yi)+xi))inside=!inside;}if(inside)pixel(c,x,y,color);} }
const crcTable=Array.from({length:256},(_,n)=>{let c=n;for(let k=0;k<8;k++)c=c&1?0xedb88320^(c>>>1):c>>>1;return c>>>0;});
function crc32(buf){let c=0xffffffff;for(const b of buf)c=crcTable[(c^b)&255]^(c>>>8);return(c^0xffffffff)>>>0;}
function chunk(type,data){const t=Buffer.from(type),len=Buffer.alloc(4),sum=Buffer.alloc(4);len.writeUInt32BE(data.length);sum.writeUInt32BE(crc32(Buffer.concat([t,data])));return Buffer.concat([len,t,data,sum]);}
function save(c,name){const rows=Buffer.alloc((size*4+1)*size);for(let y=0;y<size;y++)c.data.copy(rows,y*(size*4+1)+1,y*size*4,(y+1)*size*4);const head=Buffer.alloc(13);head.writeUInt32BE(size,0);head.writeUInt32BE(size,4);head[8]=8;head[9]=6;const png=Buffer.concat([Buffer.from([137,80,78,71,13,10,26,10]),chunk('IHDR',head),chunk('IDAT',zlib.deflateSync(rows)),chunk('IEND',Buffer.alloc(0))]);fs.writeFileSync(name,png);}

const out=path.resolve('VioletStudio.Web/wwwroot/assets');fs.mkdirSync(out,{recursive:true});
let c=canvas();rounded(c,8,26,112,76,20,[255,0,0]);polygon(c,[[52,43],[52,85],[88,64]],[255,255,255]);save(c,path.join(out,'youtube.png'));
c=canvas();rounded(c,8,8,112,112,31,[220,53,126],(x,y)=>{const t=Math.max(0,Math.min(1,(x+y-16)/224));const a=[255,194,55],b=[105,46,195];return a.map((v,i)=>Math.round(v*(1-t)+b[i]*t));});rounded(c,28,28,72,72,22,[255,255,255],(x,y)=>{const qx=Math.max(50,Math.min(x,78)),qy=Math.max(50,Math.min(y,78));return (x-qx)**2+(y-qy)**2<=22**2&&(()=>{const ix=Math.max(35,Math.min(x,93)),iy=Math.max(35,Math.min(y,93));return (x-ix)**2+(y-iy)**2>16**2;})();});circle(c,64,64,19,[255,255,255]);circle(c,64,64,13,[173,63,162]);circle(c,91,39,5,[255,255,255]);save(c,path.join(out,'instagram.png'));
c=canvas();const cyan=[37,244,238],pink=[254,44,85],white=[255,255,255];line(c,77,20,77,79,16,cyan);line(c,77,22,98,28,16,cyan);circle(c,49,84,19,cyan);line(c,71,17,71,76,16,pink);line(c,71,19,92,25,16,pink);circle(c,43,81,19,pink);line(c,74,18,74,77,14,white);line(c,74,21,95,27,14,white);circle(c,46,83,16,white);save(c,path.join(out,'tiktok.png'));

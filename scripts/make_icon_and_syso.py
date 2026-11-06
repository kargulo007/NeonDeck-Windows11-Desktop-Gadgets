from PIL import Image, ImageDraw, ImageFilter
from pathlib import Path
import struct
base=Path(__file__).resolve().parents[1]
# Render a high-resolution, sharply scaled native application icon.
S=1024
im=Image.new('RGBA',(S,S),(0,0,0,0))
d=ImageDraw.Draw(im)
# tile and restrained teal edge
D=(86,86,938,938)
d.rounded_rectangle(D, radius=205, fill=(9,27,30,255),outline=(60,204,174,255),width=28)
d.rounded_rectangle((113,113,911,911),radius=179,outline=(24,80,80,255),width=16)
# monitoring bars
for x,top,c in [(278,518,(53,201,178,255)),(435,363,(95,235,179,255)),(592,441,(235,186,60,255))]:
 d.rounded_rectangle((x,top,x+99,729),radius=29,fill=c)
# heartbeat trace over bars, dark contrast and bright signal
def path(points, fill, width): d.line(points, fill=fill, width=width, joint='curve')
pts=[(200,520),(315,520),(382,456),(461,602),(537,424),(595,520),(825,520)]
path(pts,(6,25,28,255),44)
path(pts,(237,203,71,255),29)
for x,y in [(200,520),(825,520)]: d.ellipse((x-15,y-15,x+15,y+15),fill=(250,220,88,255))
# corner accent light
d.ellipse((767,231,814,278),fill=(73,225,191,255))
im.save(base/'NeonDeck_icon.png')
sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)]
im.save(base/'NeonDeck.ico',sizes=sizes,format='ICO')
# Build Win32 RT_ICON + RT_GROUP_ICON from ICO data into COFF .rsrc (.syso)
raw=(base/'NeonDeck.ico').read_bytes()
reserved,kind,count=struct.unpack_from('<HHH',raw,0)
assert reserved==0 and kind==1 and count>=4
items=[]
for i in range(count):
 w,h,colors,rsrv,planes,bpp,length,offset=struct.unpack_from('<BBBBHHII',raw,6+i*16)
 items.append({'w':w,'h':h,'colors':colors,'planes':planes,'bpp':bpp,'data':raw[offset:offset+length]})
assert all(it['data'] for it in items)
# Resource directory record tree. One RT_ICON per ICO entry, one RT_GROUP_ICON.
# layout: directory tables (root, type dirs, icon-id dirs, group-id dir)
buf=bytearray()
def pad(a=4):
 while len(buf)%a: buf.append(0)
def alloc(size,align=4):
 pad(align); off=len(buf); buf.extend(b'\0'*size);return off
root=alloc(16+2*8)
icons_type=alloc(16+len(items)*8)
group_type=alloc(16+8)
icon_ids=[alloc(16+8) for _ in items]
group_id=alloc(16+8)
icon_data_entries=[alloc(16) for _ in items]
group_data_entry=alloc(16)
def directory(off, n_entries):
 struct.pack_into('<IIHHHH',buf,off,0,0,0,0,0,n_entries)
def entry(dir_off,index,rid,target,subdir):
 struct.pack_into('<II',buf,dir_off+16+8*index,rid,target | (0x80000000 if subdir else 0))
directory(root,2);entry(root,0,3,icons_type,True);entry(root,1,14,group_type,True)
directory(icons_type,len(items));directory(group_type,1)
for i,id_dir in enumerate(icon_ids):
 entry(icons_type,i,i+1,id_dir,True)
 directory(id_dir,1);entry(id_dir,0,1033,icon_data_entries[i],False)
entry(group_type,0,1,group_id,True)
directory(group_id,1);entry(group_id,0,1033,group_data_entry,False)
relocs=[]
for i,item in enumerate(items):
 off=alloc(len(item['data']))
 buf[off:off+len(item['data'])]=item['data']
 struct.pack_into('<IIII',buf,icon_data_entries[i],off,len(item['data']),0,0)
 relocs.append(icon_data_entries[i])
# group icon is GRPICONDIR + GRPICONDIRENTRY[N]
grp=bytearray(struct.pack('<HHH',0,1,len(items)))
for i,item in enumerate(items):
 grp+=struct.pack('<BBBBHHIH',item['w'],item['h'],item['colors'],0,item['planes'],item['bpp'],len(item['data']),i+1)
goff=alloc(len(grp));buf[goff:goff+len(grp)]=grp
struct.pack_into('<IIII',buf,group_data_entry,goff,len(grp),0,0)
relocs.append(group_data_entry)
# x64 COFF file, .rsrc section, one static section symbol, relocations IMAGE_REL_AMD64_ADDR32NB (3)
coff_header_size=20;section_header_size=40
raw_ptr=coff_header_size+section_header_size
reloc_ptr=raw_ptr+len(buf)
symbol_ptr=reloc_ptr+len(relocs)*10
s=bytearray()
s.extend(struct.pack('<HHIIIHH',0x8664,1,0,symbol_ptr,1,0,0))
s.extend(struct.pack('<8sIIIIIIHHI',b'.rsrc\0\0\0',len(buf),0,len(buf),raw_ptr,reloc_ptr,0,len(relocs),0,0x40000040))
s.extend(buf)
for r in relocs: s.extend(struct.pack('<IIH',r,0,3))
s.extend(struct.pack('<8sIhHBB',b'.rsrc\0\0\0',0,1,0,3,0))
s.extend(struct.pack('<I',4))
resource_dir = (base/'launcher') if (base/'launcher').is_dir() else (base/'buildtools')
resource_dir.mkdir(parents=True, exist_ok=True)
(resource_dir/'rsrc_windows_amd64.syso').write_bytes(s)
print(f'Generated PNG and ICO ({len(items)} icon sizes) and COFF resource object {len(s)} bytes')

import sys, dnfile, struct

PATH = r"H:\steam\steamapps\common\RimWorld\Assembly-CSharp.dll"
pe = dnfile.dnPE(PATH)
mdt = pe.net.mdtables


def read_blob(idx):
    return pe.get_data(pe.net.metadata.streams.get_by_name('#Blob').get_file_offset() + pe.net.metadata.streams.get_by_name('#Blob').get_offset() + idx)

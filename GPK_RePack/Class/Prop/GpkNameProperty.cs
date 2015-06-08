﻿namespace GPK_RePack.Class.Prop
{
    class GpkNameProperty : GpkBaseProperty
    {
        public long unk;
        public string value; //long index
        public int padding;

        public GpkNameProperty()
        {

        }
        public GpkNameProperty(GpkBaseProperty bp)
        {
            Name = bp.Name;
            type = bp.type;
        }

        public override string ToString()
        {
            return string.Format("Name: {0} Type: {1} Value: {2}", Name, type, value);
        }
    }

}
